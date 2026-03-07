using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if DOTWEEN_ENABLED
using DG.Tweening;
#endif

// Dynamic score popup system for line clears.
// Uses pooling + TMP texts and DoTween sequence animation when DOTWEEN is available.
public class ScorePopupManager : MonoBehaviour
{
    public static ScorePopupManager Instance { get; private set; }

    [Header("Pool")]
    [SerializeField] int prewarmCount = 24;

    [Header("Visual")]
    [SerializeField] Color popupColor = new Color(1f, 0.92f, 0.23f, 1f);
    [SerializeField] float moveUpDistance = 190f;
    [SerializeField] float popDuration = 0.12f;
    [SerializeField] float moveDuration = 0.60f;
    [SerializeField] float fadeDelay = 0.50f;
    [SerializeField] float fadeDuration = 0.25f;

    readonly Queue<PopupItem> _pool = new Queue<PopupItem>();
    RectTransform _layer;
    Canvas _canvas;
    float _lastPopupTime;
    int _lastPopupDelta;
    Vector3 _lastPopupWorldPos;

    class PopupItem
    {
        public GameObject go;
        public RectTransform rt;
        public TextMeshProUGUI text;
        public CanvasGroup group;
#if DOTWEEN_ENABLED
        public Sequence seq;
#endif
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        TrySetupLayer();
        Prewarm();
    }

    void OnEnable()
    {
        StartCoroutine(BindComboManager());
    }

    void OnDisable()
    {
        if (ComboManager.Instance != null)
            ComboManager.Instance.ScorePopupRequested -= HandleScorePopup;
    }

    IEnumerator BindComboManager()
    {
        while (ComboManager.Instance == null)
            yield return null;

        ComboManager.Instance.ScorePopupRequested -= HandleScorePopup;
        ComboManager.Instance.ScorePopupRequested += HandleScorePopup;
    }

    void TrySetupLayer()
    {
        _canvas = FindObjectOfType<Canvas>();
        if (_canvas == null) return;

        Transform existing = _canvas.transform.Find("ScorePopupLayer");
        if (existing != null)
        {
            _layer = existing as RectTransform;
            return;
        }

        var go = new GameObject("ScorePopupLayer");
        go.transform.SetParent(_canvas.transform, false);
        go.transform.SetAsLastSibling();

        _layer = go.AddComponent<RectTransform>();
        _layer.anchorMin = Vector2.zero;
        _layer.anchorMax = Vector2.one;
        _layer.offsetMin = Vector2.zero;
        _layer.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
    }

    void Prewarm()
    {
        if (_layer == null) return;

        for (int i = 0; i < prewarmCount; i++)
            _pool.Enqueue(CreatePopupItem());
    }

    PopupItem CreatePopupItem()
    {
        var go = new GameObject("ScorePopup");
        go.transform.SetParent(_layer, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(340f, 110f);

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.color = popupColor;

        go.SetActive(false);

        return new PopupItem
        {
            go = go,
            rt = rt,
            text = txt,
            group = group
        };
    }

    PopupItem GetPopup()
    {
        if (_pool.Count > 0) return _pool.Dequeue();
        return CreatePopupItem();
    }

    void ReleasePopup(PopupItem item)
    {
        if (item == null || item.go == null) return;

#if DOTWEEN_ENABLED
        item.seq?.Kill();
        item.seq = null;
#endif

        item.go.SetActive(false);
        item.group.alpha = 0f;
        item.rt.localScale = Vector3.one;
        _pool.Enqueue(item);
    }

    void HandleScorePopup(int delta, Vector3 worldPos)
    {
        if (delta <= 0) return;

        // Guard against accidental duplicate event dispatches in the same Mega Bang frame.
        // If same score appears at almost same position within a very short window, ignore the second one.
        if (Mathf.Abs(Time.unscaledTime - _lastPopupTime) < 0.12f &&
            _lastPopupDelta == delta &&
            Vector3.SqrMagnitude(_lastPopupWorldPos - worldPos) < 0.0004f)
            return;

        _lastPopupTime = Time.unscaledTime;
        _lastPopupDelta = delta;
        _lastPopupWorldPos = worldPos;

        if (_canvas == null || _layer == null || Camera.main == null)
        {
            TrySetupLayer();
            if (_canvas == null || _layer == null || Camera.main == null) return;
        }

        Vector2 screenPt = Camera.main.WorldToScreenPoint(worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(), screenPt, _canvas.worldCamera, out Vector2 localPt);

        PopupItem item = GetPopup();
        item.go.SetActive(true);

        item.text.text = "+" + delta.ToString("N0");
        item.text.fontSize = delta >= 300 ? 72f : delta >= 100 ? 62f : 52f;
        item.text.color = popupColor;

        item.rt.anchoredPosition = localPt;
        item.rt.localScale = Vector3.one * 0.55f;
        item.group.alpha = 1f;

#if DOTWEEN_ENABLED
        item.seq?.Kill();
        Vector2 target = localPt + new Vector2(0f, moveUpDistance);

        item.seq = DOTween.Sequence();
        item.seq.Join(item.rt.DOScale(1.08f, popDuration).SetEase(Ease.OutBack));
        item.seq.Join(item.rt.DOAnchorPos(target, moveDuration).SetEase(Ease.OutCubic));
        item.seq.AppendInterval(Mathf.Max(0f, fadeDelay - popDuration));
        item.seq.Append(item.group.DOFade(0f, fadeDuration).SetEase(Ease.OutQuad));
        item.seq.OnComplete(() => ReleasePopup(item));
#else
        StartCoroutine(FallbackAnimate(item, localPt));
#endif
    }

#if !DOTWEEN_ENABLED
    IEnumerator FallbackAnimate(PopupItem item, Vector2 start)
    {
        Vector2 end = start + new Vector2(0f, moveUpDistance);

        float t = 0f;
        while (t < moveDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / moveDuration);
            item.rt.anchoredPosition = Vector2.Lerp(start, end, 1f - Mathf.Pow(1f - u, 3f));

            float sT = Mathf.Clamp01(t / popDuration);
            float scale = Mathf.Lerp(0.55f, 1.08f, sT);
            item.rt.localScale = Vector3.one * scale;

            if (t > fadeDelay)
            {
                float aT = Mathf.Clamp01((t - fadeDelay) / fadeDuration);
                item.group.alpha = Mathf.Lerp(1f, 0f, aT);
            }

            yield return null;
        }

        ReleasePopup(item);
    }
#endif
}
