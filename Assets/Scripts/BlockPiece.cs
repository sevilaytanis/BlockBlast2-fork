using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One draggable block shape.
// Created entirely at runtime by TrayManager - no prefab required.
public class BlockPiece : MonoBehaviour
{
    // ── Public read-only data ─────────────────────────────────────────────────

    public Vector2Int[] Offsets       { get; private set; }
    public int          TraySlotIndex { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private List<GameObject> _squares    = new List<GameObject>();
    private Vector3          _trayPos;
    private Vector3          _dragOffset;
    private bool             _isDragging;
    private Transform        _originalParent;
    private int              _originalSiblingIndex;
    private Vector3          _originalLocalScale;
    private Vector3          _originalWorldPosition;
    private bool             _hasOriginalState;

    // Track last preview anchor to avoid rebuilding preview every frame
    private Vector2Int _lastPreviewAnchor = new Vector2Int(int.MinValue, int.MinValue);

    // Scale used while sitting in the tray (restored on ReturnToTray)
    private float _trayScale = 1f;

    // Idle float coroutine reference so we can stop it on drag
    private Coroutine _idleCoroutine;

    // ShakeAndReturn coroutine — tracked so BeginDrag can cancel it
    private Coroutine _returnCoroutine;
    private bool _trayBreathPaused;
    private float _footprintMinLocalX;
    private float _footprintMaxLocalX;
    private float _footprintMinLocalY;
    private float _footprintMaxLocalY;
    private Color _pieceBaseColor;
    private Color _cachedDragPreviewColor;

    // Shared sprite - created once, reused by every block
    private static Sprite _sharedSprite;
    private static Transform _dragLayer;

    [Header("Invalid Drop Feedback")]
    [SerializeField] private float invalidShakeDuration = 0.12f;
    [SerializeField] private float invalidShakeAmplitudePx = 8f;
    [SerializeField] private float invalidReturnDuration = 0.12f;
    [SerializeField] private int invalidShakeCycles = 3;
    [SerializeField] private bool clampToTrayWhileDragging = false;
    [SerializeField] private float placeBounceDuration = 0.15f;
    [SerializeField] private float placeBounceScale = 1.08f;
    [SerializeField, Range(0.70f, 0.80f)] private float previewDarkenFactor = 0.75f;
    [SerializeField, Range(0.35f, 0.55f)] private float previewAlpha = 0.45f;

    // ── Initialization (called by TrayManager) ────────────────────────────────

    public void Initialize(Vector2Int[] offsets, Color color, Vector3 trayPos, int slotIndex,
                           float trayScale = 1f)
    {
        Offsets       = offsets;
        TraySlotIndex = slotIndex;
        _pieceBaseColor = color;
        float cs = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;

        ShapeFootprintUtility.BoundsInt2D bounds = ShapeFootprintUtility.CalculateBounds(offsets);
        CacheFootprintExtents(bounds, cs);

        _trayScale = trayScale;
        if (TrayManager.Instance != null &&
            TrayManager.Instance.TryGetSlotRectWorld(slotIndex, out Rect slotRect))
        {
            _trayScale = ShapeFootprintUtility.ComputeFitScale(
                slotWorldSize: new Vector2(slotRect.width, slotRect.height),
                cells: offsets,
                cellSize: cs,
                maxScale: trayScale,
                fill: 0.90f);
        }

        transform.localScale = Vector3.one * _trayScale;

        // Center piece root by footprint center so any shape is visually centered in its slot.
        float bbCx = (bounds.minX + bounds.maxX) * 0.5f * cs * _trayScale;
        float bbCy = (bounds.minY + bounds.maxY) * 0.5f * cs * _trayScale;
        Vector3 centeredPos = trayPos - new Vector3(bbCx, bbCy, 0f);

        _trayPos           = centeredPos;   // store adjusted home so animations are correct
        transform.position = centeredPos;

        if (_sharedSprite == null)
            _sharedSprite = GridManager.MakeSquareSprite();

        BuildSquares(color);

        // Gentle idle bob to signal interactivity
        _idleCoroutine = StartCoroutine(IdleFloat());
    }

    void BuildSquares(Color color)
    {
        float cs = GridManager.Instance.cellSize;

        for (int i = 0; i < Offsets.Length; i++)
        {
            var sq = new GameObject("Sq_" + i);
            sq.transform.parent        = transform;
            sq.transform.localPosition = new Vector3(Offsets[i].x * cs, Offsets[i].y * cs, 0f);
            sq.transform.localScale    = Vector3.one * cs * 0.9f;

            var sr          = sq.AddComponent<SpriteRenderer>();
            sr.sprite       = _sharedSprite;
            sr.color        = color;
            sr.sortingOrder = 1;

            // Soft 3D depth: top highlight + bottom shadow child layers.
            // Children are tinted white/black and sit in the sq's local space
            // where the sprite spans ±0.5 local units on each axis.
            AddDepthLayer(sq, isHighlight: true,  baseSortOrder: 1);
            AddDepthLayer(sq, isHighlight: false, baseSortOrder: 1);

            sq.AddComponent<BoxCollider2D>();
            sq.AddComponent<SquareDragProxy>();

            _squares.Add(sq);
        }
    }

    // White (highlight) or black (shadow) strip overlaid on a block square.
    // localPosition / localScale are in sq-local space: sprite occupies ±0.5 units.
    //   Highlight: top 28%  → center y=+0.36, height 0.28
    //   Shadow:    bottom 22% → center y=-0.39, height 0.22
    // Width 0.86 keeps strips inside the rounded corners.
    static void AddDepthLayer(GameObject sq, bool isHighlight, int baseSortOrder)
    {
        var layer = new GameObject(isHighlight ? "Highlight" : "Shadow");
        layer.transform.SetParent(sq.transform, false);
        layer.transform.localPosition = new Vector3(0f, isHighlight ? 0.36f : -0.39f, 0f);
        layer.transform.localScale    = new Vector3(0.86f, isHighlight ? 0.28f : 0.22f, 1f);

        var sr          = layer.AddComponent<SpriteRenderer>();
        sr.sprite       = GridManager.GetOrCreateSprite();
        sr.color        = isHighlight
                          ? new Color(1f, 1f, 1f, 0.24f)   // soft white top highlight
                          : new Color(0f, 0f, 0f, 0.20f);  // soft black bottom shadow
        sr.sortingOrder = baseSortOrder + 1;
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    // Güvenlik ağı: OnMouseUp kaçırılırsa (hızlı parmak kaldırma, sahne geçişi, vb.)
    // Update her frame kontrol ederek sürükleme durumunu düzeltir.
    void Update()
    {
        if (!_isDragging) return;

        // Touch/mouse-safe drag update: keep piece following pointer even if OnMouseDrag misses a frame.
        DuringDrag();

        bool released = Input.GetMouseButtonUp(0);

        // Dokunmatik ekran desteği — birden fazla parmak, herhangi biri kalkmışsa serbest bırak
        if (!released && Input.touchCount > 0)
        {
            foreach (var touch in Input.touches)
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                { released = true; break; }
        }

        if (released) EndDrag();
    }

    // ── Drag API (called by SquareDragProxy) ──────────────────────────────────

    public void BeginDrag()
    {
        if (_isDragging) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        _isDragging = true;
        _cachedDragPreviewColor = BuildPreviewColor(_pieceBaseColor);

        // Stop idle float, reset to tray base Y
        if (_idleCoroutine != null) { StopCoroutine(_idleCoroutine); _idleCoroutine = null; }

        // Parça geri dönme animasyonunu iptal et (hızlı ikinci dokunuş için)
        if (_returnCoroutine != null) { StopCoroutine(_returnCoroutine); _returnCoroutine = null; }

        StoreOriginalState();
        MoveToDragLayer();

        // Keep the initial contact point under the finger/mouse without jump.
        _dragOffset  = transform.position - PointerWorld();

        if (!_trayBreathPaused)
        {
            TrayContainerBreath.PauseAll();
            _trayBreathPaused = true;
        }

        SetSortOrder(10);
        StartCoroutine(PickupSpring());
    }

    public void DuringDrag()
    {
        if (!_isDragging) return;

        Vector3 desired = PointerWorld() + _dragOffset;
        if (clampToTrayWhileDragging && !GridManager.Instance.TryWorldToCell(desired, out Vector2Int anchor))
            desired = ClampInsideTrayBounds(desired);

        transform.position = desired;

        if (!GridManager.Instance.TryWorldToCell(desired, out anchor))
        {
            if (_lastPreviewAnchor.x != int.MinValue)
            {
                GridManager.Instance.ClearPreview();
                _lastPreviewAnchor = new Vector2Int(int.MinValue, int.MinValue);
            }
            return;
        }

        // Update preview only when we move to a new snapped cell.
        if (anchor != _lastPreviewAnchor)
        {
            _lastPreviewAnchor = anchor;
            GridManager.Instance.ShowPreview(anchor, Offsets, _cachedDragPreviewColor);
        }
    }

    public void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        ResumeTrayBreathIfNeeded();

        GridManager.Instance.ClearPreview();
        _lastPreviewAnchor = new Vector2Int(int.MinValue, int.MinValue);

        bool hasCell = GridManager.Instance.TryWorldToCell(transform.position, out Vector2Int anchor);
        if (hasCell && GridManager.Instance.CanPlaceAt(anchor, Offsets))
            StartCoroutine(PlaceWithFeedback(anchor));
        else
        {
            AudioManager.Instance?.PlaySfx("invalid");
            _returnCoroutine = StartCoroutine(ShakeAndReturn());
        }
    }

    void OnDisable() => ResumeTrayBreathIfNeeded();

    // ── Placement ─────────────────────────────────────────────────────────────

    IEnumerator PlaceWithFeedback(Vector2Int anchor)
    {
        // Hook requested at success decision point.
        AudioManager.Instance?.PlaySfx("place");
        yield return StartCoroutine(Bounce(transform));
        PlaceOnBoard(anchor);
    }

    void PlaceOnBoard(Vector2Int anchor)
    {
        SetSortOrder(1);

        foreach (var sq in _squares)
        {
            Destroy(sq.GetComponent<SquareDragProxy>());
            Destroy(sq.GetComponent<BoxCollider2D>());
            sq.transform.SetParent(GridManager.Instance.transform, worldPositionStays: true);
        }

        StartCoroutine(PlaceRoutine(anchor));
    }

    IEnumerator PlaceRoutine(Vector2Int anchor)
    {
        // Wait for grid animation to finish before notifying the tray
        yield return StartCoroutine(
            GridManager.Instance.PlaceBlockRoutine(anchor, Offsets, _squares));

        TrayManager.Instance.OnBlockPlaced(TraySlotIndex);
        Destroy(gameObject);
    }

    // ── Animations ────────────────────────────────────────────────────────────

    // Spring scale on pickup: trayScale → 1.12 → 1.0
    IEnumerator PickupSpring()
    {
        float t = 0f;
        float duration = 0.12f;
        float peak = 1.12f;

        // Phase 1: scale up to peak
        while (t < duration * 0.5f)
        {
            t += Time.deltaTime;
            float frac = t / (duration * 0.5f);
            transform.localScale = Vector3.one * Mathf.Lerp(_trayScale, peak, frac);
            yield return null;
        }

        // Phase 2: settle to 1.0
        t = 0f;
        while (t < duration * 0.5f)
        {
            t += Time.deltaTime;
            float frac = t / (duration * 0.5f);
            transform.localScale = Vector3.one * Mathf.Lerp(peak, 1f, frac);
            yield return null;
        }

        transform.localScale = Vector3.one;
    }

    // Horizontal shake then lerp back to tray on invalid drop
    IEnumerator ShakeAndReturn()
    {
        yield return StartCoroutine(Shake(transform));

        RestoreParentAndSibling();

        // Lerp back to tray position
        float returnDuration = invalidReturnDuration;
        float elapsed = 0f;
        Vector3 shakeEnd = transform.position;
        Vector3 targetPos = _hasOriginalState ? _originalWorldPosition : _trayPos;
        Vector3 targetScale = _hasOriginalState ? _originalLocalScale : (Vector3.one * _trayScale);
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float frac = elapsed / returnDuration;
            transform.position   = Vector3.Lerp(shakeEnd, targetPos, frac);
            transform.localScale = Vector3.Lerp(Vector3.one, targetScale, frac);
            yield return null;
        }

        transform.position   = targetPos;
        transform.localScale = targetScale;
        _trayPos             = targetPos;
        _trayScale           = targetScale.x;

        if (TrayManager.Instance != null &&
            TrayManager.Instance.TryGetSlotRectWorld(TraySlotIndex, out Rect slotRect))
            FitToTraySlot(slotRect);

        SetSortOrder(1);

        _returnCoroutine = null; // animasyon bitti, referansı temizle

        // Resume idle float
        _idleCoroutine = StartCoroutine(IdleFloat());
    }

    IEnumerator Bounce(Transform t)
    {
        Vector3 baseScale = t.localScale;
        Vector3 peakScale = baseScale * placeBounceScale;
        float half = placeBounceDuration * 0.5f;
        float elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float frac = Mathf.Clamp01(elapsed / Mathf.Max(half, 0.0001f));
            float ease = 1f - Mathf.Pow(1f - frac, 2f);
            t.localScale = Vector3.Lerp(baseScale, peakScale, ease);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float frac = Mathf.Clamp01(elapsed / Mathf.Max(half, 0.0001f));
            float ease = 1f - Mathf.Pow(1f - frac, 2f);
            t.localScale = Vector3.Lerp(peakScale, baseScale, ease);
            yield return null;
        }

        t.localScale = baseScale;
    }

    IEnumerator Shake(Transform t)
    {
        Vector3 startPos = t.position;
        float shakeAmp = PixelToWorldX(invalidShakeAmplitudePx);
        float elapsed = 0f;

        while (elapsed < invalidShakeDuration)
        {
            elapsed += Time.deltaTime;
            float frac = Mathf.Clamp01(elapsed / Mathf.Max(invalidShakeDuration, 0.0001f));
            float angle = frac * invalidShakeCycles * 2f * Mathf.PI;
            float xOff = Mathf.Sin(angle) * shakeAmp * (1f - frac);
            t.position = startPos + new Vector3(xOff, 0f, 0f);
            yield return null;
        }

        t.position = startPos;
    }

    // Gentle Y sine-wave bob while sitting in the tray
    IEnumerator IdleFloat()
    {
        // 1.5 % of camera half-height gives ~7-10 px bob on all devices (iPhone 8 → iPad).
        float amp    = Camera.main != null ? Camera.main.orthographicSize * 0.015f : 0.04f;
        float period = 2.2f;
        float offset = Random.Range(0f, Mathf.PI * 2f); // stagger pieces
        float t      = offset;

        while (true)
        {
            t += Time.deltaTime;
            float y = _trayPos.y + Mathf.Sin(t * (2f * Mathf.PI / period)) * amp;
            transform.position = new Vector3(_trayPos.x, y, _trayPos.z);
            yield return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Vector3 PointerWorld()
    {
        Vector3 screenPos = Input.mousePosition;
        if (Input.touchCount > 0)
            screenPos = Input.GetTouch(0).position;

        Vector3 p = Camera.main.ScreenToWorldPoint(screenPos);
        p.z = 0f;
        return p;
    }

    static float PixelToWorldX(float px)
    {
        if (Camera.main == null || Screen.width <= 0) return 0f;
        float worldWidth = Camera.main.orthographicSize * 2f * Camera.main.aspect;
        return worldWidth * (px / Screen.width);
    }

    Color BuildPreviewColor(Color baseColor)
    {
        float f = Mathf.Clamp(previewDarkenFactor, 0.70f, 0.80f);
        float a = Mathf.Clamp(previewAlpha, 0.35f, 0.55f);
        return new Color(baseColor.r * f, baseColor.g * f, baseColor.b * f, a);
    }

    static Transform GetOrCreateDragLayer()
    {
        if (_dragLayer != null) return _dragLayer;
        var existing = GameObject.Find("DragLayer");
        if (existing != null)
        {
            _dragLayer = existing.transform;
            return _dragLayer;
        }

        var go = new GameObject("DragLayer");
        _dragLayer = go.transform;
        return _dragLayer;
    }

    void SetSortOrder(int order)
    {
        foreach (var sq in _squares)
        {
            if (sq == null) continue;
            // Base square
            if (sq.TryGetComponent<SpriteRenderer>(out var baseSr))
                baseSr.sortingOrder = order;
            // Depth layer children (Highlight, Shadow) render one above base
            foreach (Transform child in sq.transform)
                if (child.TryGetComponent<SpriteRenderer>(out var childSr))
                    childSr.sortingOrder = order + 1;
        }
    }

    void ResumeTrayBreathIfNeeded()
    {
        if (!_trayBreathPaused) return;
        TrayContainerBreath.ResumeAll();
        _trayBreathPaused = false;
    }

    void StoreOriginalState()
    {
        _originalParent = transform.parent;
        _originalSiblingIndex = transform.GetSiblingIndex();
        _originalLocalScale = transform.localScale;
        _originalWorldPosition = transform.position;
        _hasOriginalState = true;
    }

    void MoveToDragLayer()
    {
        Transform layer = GetOrCreateDragLayer();
        transform.SetParent(layer, worldPositionStays: true);
        transform.SetAsLastSibling();
    }

    void RestoreParentAndSibling()
    {
        if (!_hasOriginalState) return;
        if (_originalParent == null) return;

        transform.SetParent(_originalParent, worldPositionStays: true);
        int maxIdx = Mathf.Max(0, _originalParent.childCount - 1);
        transform.SetSiblingIndex(Mathf.Clamp(_originalSiblingIndex, 0, maxIdx));
    }

    // Called at tray rest points only (spawn / invalid return), not while dragging over grid.
    public void FitToTraySlot(Rect slotRectWorld)
    {
        TrayPieceFitter.FitToSlot(transform, slotRectWorld, Offsets, TraySlotIndex);
        _trayPos = transform.position;
        _trayScale = transform.localScale.x;
    }

    void CacheFootprintExtents(ShapeFootprintUtility.BoundsInt2D bounds, float cellSize)
    {
        // Use +/- 0.5 cell around center points so full cell footprint is clamped.
        _footprintMinLocalX = (bounds.minX - 0.5f) * cellSize;
        _footprintMaxLocalX = (bounds.maxX + 0.5f) * cellSize;
        _footprintMinLocalY = (bounds.minY - 0.5f) * cellSize;
        _footprintMaxLocalY = (bounds.maxY + 0.5f) * cellSize;
    }

    Vector3 ClampInsideTrayBounds(Vector3 worldPos)
    {
        if (GridManager.Instance == null) return worldPos;
        if (!GridManager.Instance.TryGetTrayBoundsWorld(out Rect trayBounds)) return worldPos;

        float scale = transform.localScale.x;

        float minX = worldPos.x + _footprintMinLocalX * scale;
        float maxX = worldPos.x + _footprintMaxLocalX * scale;
        float minY = worldPos.y + _footprintMinLocalY * scale;
        float maxY = worldPos.y + _footprintMaxLocalY * scale;

        if (minX < trayBounds.xMin) worldPos.x += trayBounds.xMin - minX;
        if (maxX > trayBounds.xMax) worldPos.x -= maxX - trayBounds.xMax;
        if (minY < trayBounds.yMin) worldPos.y += trayBounds.yMin - minY;
        if (maxY > trayBounds.yMax) worldPos.y -= maxY - trayBounds.yMax;

        return worldPos;
    }
}
