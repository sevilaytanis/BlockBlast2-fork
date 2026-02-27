using UnityEngine;

// Creates/updates a SafeAreaRoot under the Canvas and keeps it aligned to Screen.safeArea.
// UI elements parented under SafeAreaRoot stay inside notches/home-indicator bounds.
[DefaultExecutionOrder(-1000)]
public class ApplySafeArea : MonoBehaviour
{
    [SerializeField] private RectTransform safeAreaRoot;
    private Rect _lastSafeArea;
    private Vector2Int _lastScreenSize;

    public RectTransform SafeAreaRoot => safeAreaRoot;

    void Awake()
    {
        EnsureSafeAreaRoot();
        Apply();
    }

    void OnEnable()
    {
        EnsureSafeAreaRoot();
        Apply();
    }

    void LateUpdate()
    {
        if (_lastSafeArea != Screen.safeArea || _lastScreenSize.x != Screen.width || _lastScreenSize.y != Screen.height)
            Apply();
    }

    void EnsureSafeAreaRoot()
    {
        if (safeAreaRoot != null) return;

        Transform existing = transform.Find("SafeAreaRoot");
        if (existing != null)
        {
            safeAreaRoot = existing as RectTransform;
            return;
        }

        var go = new GameObject("SafeAreaRoot", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        safeAreaRoot = go.GetComponent<RectTransform>();
        safeAreaRoot.anchorMin = Vector2.zero;
        safeAreaRoot.anchorMax = Vector2.one;
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
    }

    void Apply()
    {
        if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0) return;

        Rect safe = Screen.safeArea;
        Vector2 anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        Vector2 anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);

        safeAreaRoot.anchorMin = anchorMin;
        safeAreaRoot.anchorMax = anchorMax;
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;

        _lastSafeArea = safe;
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    }

    public static RectTransform GetOrCreateSafeAreaRoot(Canvas canvas)
    {
        if (canvas == null) return null;
        var comp = canvas.GetComponent<ApplySafeArea>();
        if (comp == null) comp = canvas.gameObject.AddComponent<ApplySafeArea>();
        comp.EnsureSafeAreaRoot();
        comp.Apply();
        return comp.SafeAreaRoot;
    }
}

