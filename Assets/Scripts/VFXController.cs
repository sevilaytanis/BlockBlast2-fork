using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if DOTWEEN_ENABLED
using DG.Tweening;
#endif

// Lightweight sparkle + bounce controller for block placement and clear events.
// Uses pooling to avoid runtime alloc/instantiate spikes.
public class VFXController : MonoBehaviour
{
    public static VFXController Instance { get; private set; }

    [Header("Sparkle Pool")]
    [SerializeField] private int prewarmSparkles = 64;
    [SerializeField] private int maxClearSparkles = 48;
    [SerializeField] private bool enableSparkles = false;

    [Header("Sparkle Motion")]
    [SerializeField] private float sparkleLife = 0.32f;
    [SerializeField] private float sparkleMinSpeed = 0.4f;
    [SerializeField] private float sparkleMaxSpeed = 1.6f;
    [SerializeField] private float sparkleMinScale = 0.06f;
    [SerializeField] private float sparkleMaxScale = 0.13f;

    readonly Queue<SpriteRenderer> _sparklePool = new Queue<SpriteRenderer>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Prewarm();
    }

    void Prewarm()
    {
        for (int i = 0; i < prewarmSparkles; i++)
        {
            var sr = CreateSparkle();
            sr.gameObject.SetActive(false);
            _sparklePool.Enqueue(sr);
        }
    }

    SpriteRenderer CreateSparkle()
    {
        var go = new GameObject("Sparkle");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GridManager.GetOrCreateSprite();
        sr.sortingOrder = 12;
        return sr;
    }

    SpriteRenderer GetSparkle()
    {
        if (_sparklePool.Count > 0)
            return _sparklePool.Dequeue();

        // Soft cap: avoid unlimited growth on low-end devices.
        if (transform.childCount > prewarmSparkles + maxClearSparkles)
            return null;

        return CreateSparkle();
    }

    // Placement sparkle burst + subtle punch bounce.
    public void PlayPlacementImpact(Vector3 worldPos, Color color, Transform blockRoot = null)
    {
        if (enableSparkles)
            SpawnSparkles(worldPos, color, 1);

#if DOTWEEN_ENABLED
        if (blockRoot != null)
            blockRoot.DOPunchScale(Vector3.one * 0.07f, 0.18f, 1, 0.5f);
#else
        if (blockRoot != null)
            StartCoroutine(FallbackPunch(blockRoot));
#endif
    }

    // Clear sparkle burst on selected cells (sampled for perf).
    public void PlayClearImpact(List<Vector3> worldPositions, Color color)
    {
        if (!enableSparkles) return;
        if (worldPositions == null || worldPositions.Count == 0) return;

        int step = Mathf.Max(1, worldPositions.Count / Mathf.Max(1, maxClearSparkles));
        for (int i = 0; i < worldPositions.Count; i += step)
            SpawnSparkles(worldPositions[i], color, 1);
    }

    void SpawnSparkles(Vector3 center, Color tint, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var sr = GetSparkle();
            if (sr == null) return;

            sr.gameObject.SetActive(true);
            sr.color = new Color(tint.r, tint.g, tint.b, 0.9f);

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(sparkleMinSpeed, sparkleMaxSpeed);
            Vector3 vel = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
            float scale = Random.Range(sparkleMinScale, sparkleMaxScale);

            sr.transform.position = center + new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.06f, 0.06f), 0f);
            sr.transform.localScale = Vector3.one * scale;
            StartCoroutine(SparkleLife(sr, vel));
        }
    }

    IEnumerator SparkleLife(SpriteRenderer sr, Vector3 velocity)
    {
        float t = 0f;
        Color c = sr.color;
        Vector3 startScale = sr.transform.localScale;

        while (t < sparkleLife && sr != null)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / sparkleLife);
            sr.transform.position += velocity * Time.deltaTime;
            velocity *= Mathf.Lerp(1f, 0.86f, u);
            sr.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, u);
            sr.color = new Color(c.r, c.g, c.b, Mathf.Lerp(c.a, 0f, u));
            yield return null;
        }

        if (sr == null) yield break;
        sr.gameObject.SetActive(false);
        _sparklePool.Enqueue(sr);
    }

#if !DOTWEEN_ENABLED
    IEnumerator FallbackPunch(Transform target)
    {
        Vector3 baseScale = target.localScale;
        Vector3 peak = baseScale * 1.08f;
        float dur = 0.18f;
        float half = dur * 0.5f;
        float t = 0f;

        while (t < half)
        {
            t += Time.deltaTime;
            target.localScale = Vector3.Lerp(baseScale, peak, t / half);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            target.localScale = Vector3.Lerp(peak, baseScale, t / half);
            yield return null;
        }

        target.localScale = baseScale;
    }
#endif
}
