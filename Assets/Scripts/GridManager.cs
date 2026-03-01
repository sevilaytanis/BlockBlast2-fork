using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owns the 8x8 board: occupancy, placement, line-clear, preview highlight.
// Attach to an empty GameObject named "GridManager".
// Execution order -100 guarantees Start() runs before TrayManager (0) and UIManager (0),
// so responsive layout is computed before any tray pieces are spawned.
[DefaultExecutionOrder(-100)]
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Dimensions")]
    public int   columns  = 8;
    public int   rows     = 8;
    public float cellSize = 1f;

    [Header("Bottom-Left Corner in World Space")]
    public Vector2 origin = new Vector2(-3.5f, -2.5f);

    [Header("Vertical Hierarchy")]
    [SerializeField] private float headerToBoardSpacingCells = 0.8f;

    [Header("Background Cell Color")]
    public Color emptyCellColor = new Color(0.082f, 0.082f, 0.102f, 1f);  // #151519 — daha pasif

    [Header("Preview Colors")]
    public Color previewValidColor   = new Color(0.000f, 0.784f, 0.588f, 0.55f); // Mint  #00C896 %55
    public Color previewInvalidColor = new Color(1.000f, 0.322f, 0.322f, 0.45f); // Coral #FF5252 %45

    [Header("Placement Feedback")]
    [SerializeField] private float placeBouncePeak = 1.08f;
    [SerializeField] private float placeBounceDuration = 0.15f;
    [SerializeField] private float boardShakeDuration = 0.10f;
    [SerializeField] private float boardShakeAmplitudeCells = 0.035f;

    // _grid[col, row] = placed square GameObject, or null when empty
    private GameObject[,] _grid;
    private static Sprite _cachedSprite;

    private Transform _boardVisualRoot;
    private Transform _boardContainerRoot;
    private Transform _trayContainerRoot;
    private Coroutine _boardShakeRoutine;
    private Rect _trayBoundsWorld;
    private bool _hasTrayBounds;

    struct PreviewCellVisual
    {
        public GameObject glowGo;
        public SpriteRenderer glowSr;
        public GameObject fillGo;
        public SpriteRenderer fillSr;
    }

    // Reused preview visuals to avoid per-frame alloc/destroy while dragging.
    private readonly List<PreviewCellVisual> _previewPool = new List<PreviewCellVisual>(12);
    private int _activePreviewCount;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        _grid    = new GameObject[columns, rows];
        // Visual building deferred to Start() so ComputeResponsiveLayout() can run first.
    }

    void Start()
    {
        // 1. Derive cellSize + layout from screen dimensions (must come before FitCamera).
        ComputeResponsiveLayout();
        // 2. Position the orthographic camera around the final layout.
        FitCamera();
        EnsureVisualRoots();
        // 3. Build visuals now that cellSize and origin are final.
        BuildBoardContainer(); // sortingOrder -3/-4 — must precede BuildBackground (-2)
        BuildBackground();
        BuildTrayContainer();  // reads updated TrayManager.slotPositions (set in step 1)
    }

    void EnsureVisualRoots()
    {
        if (_boardVisualRoot == null)
        {
            var go = new GameObject("BoardVisualRoot");
            go.transform.SetParent(transform, false);
            _boardVisualRoot = go.transform;
        }

        if (_boardContainerRoot == null)
        {
            var go = new GameObject("BoardContainer");
            go.transform.SetParent(_boardVisualRoot, false);
            _boardContainerRoot = go.transform;
        }

        if (_trayContainerRoot == null)
        {
            var go = new GameObject("TrayContainer");
            go.transform.SetParent(transform, false);
            _trayContainerRoot = go.transform;
        }
    }

    // ── Responsive layout ─────────────────────────────────────────────────────

    // Computes a cellSize that makes the grid fill exactly (1 − 2·hPad) of the screen
    // width on every device, while keeping the header + grid + tray proportions correct.
    //
    // Derivation (closed-form, no iteration):
    //   Let baseWorldH = fixedMargin + heightCoeff·cs  (from FitCamera geometry)
    //   Let k          = (topSafe + kHeaderH + kGapPx) / (kCanvasH − topSafe − kHeaderH − kGapPx)
    //   visible world width = baseWorldH·(1+k)·aspect
    //   grid width          = columns·cs
    //   grid width = visible·(1−2·hPad)
    //   → cs·(columns − heightCoeff·B) = fixedMargin·B  where B = (1+k)·aspect·(1−2·hPad)
    //   → cs = fixedMargin·B / (columns − heightCoeff·B)
    //
    // Geometry constants (must match FitCamera):
    //   fixedMargin  = 0.2    (the constant term in baseWorldH = gridTop−viewBottom)
    //   heightCoeff  = rows+2.5 = 10.5  (the cs-coefficient in baseWorldH)
    //   trayY        = origin.y − 2.4·cs  (slot centre, 2.4 cells below grid base)
    //   trayBottom   = trayY − cs        (FitCamera formula)
    //   viewBottom   = trayBottom − 0.2  (FitCamera formula)
    void ComputeResponsiveLayout()
    {
        const float kCanvasH = 1920f;
        const float kHeaderH = 228f;   // header strip height in canvas-px (≈12 % screen height)
        float kGapPx         = ResponsiveLayoutController.HeaderToGridSpacingPx();
        const float hPad     = 0.06f;  // 6 % padding on each horizontal side

        float topSafeInset = ResponsiveLayoutController.TopSafeInsetCanvas();
        float bottomSafeInset = ResponsiveLayoutController.BottomSafeInsetCanvas();

        float reservedCanvas = topSafeInset + bottomSafeInset + kHeaderH + kGapPx;
        float k      = reservedCanvas / Mathf.Max(1f, (kCanvasH - reservedCanvas));
        float aspect = (float)Screen.width / Mathf.Max(Screen.height, 1);
        float B      = (1f + k) * aspect * (1f - 2f * hPad);

        // Geometry constants derived from FitCamera tray-positioning rules.
        //   trayY        = origin.y − 2.4·cs  (2.4 cells below grid base)
        //   trayBottom   = trayY − cs          → origin.y − 3.4·cs
        //   viewBottom   = trayBottom − 0.2
        //   baseWorldH   = (rows−0.5 + 2.4 + 1.0)·cs + 0.2  = (rows+2.9)·cs + 0.2
        const float fixedMargin = 0.2f;
        float       heightCoeff = rows + 2.9f;  // = 10.9 for 8 rows

        float denom = columns - heightCoeff * B;
        if (denom > 0.01f)
            cellSize = fixedMargin * B / denom;
        // else: leave Inspector default (wide/iPad screens where width is not the constraint)

        // Center the grid horizontally at world x = 0.
        origin.x = -(columns - 1) * cellSize * 0.5f;

        // Keep a baseline Y for tray placement, then shift only the board downward
        // to create breathing space between header and board.
        float baselineOriginY = origin.y;

        // Update tray slot positions proportionally so FitCamera and BuildTrayContainer
        // both see consistent values (TrayManager.Awake() already ran, so Instance is set).
        if (TrayManager.Instance != null)
        {
            float trayY    = baselineOriginY - 2.4f * cellSize;  // keep tray baseline stable
            float slotStep = columns * cellSize * 0.35f;  // half-span = 35 % of grid width
            TrayManager.Instance.slotPositions = new Vector3[]
            {
                new Vector3(-slotStep, trayY, 0f),
                new Vector3( 0f,       trayY, 0f),
                new Vector3( slotStep, trayY, 0f),
            };
        }

        // Requested spacing constant:
        // HeaderToBoardSpacing = boardCellSize * 0.8f
        float headerToBoardSpacing = cellSize * Mathf.Max(0f, headerToBoardSpacingCells);
        origin.y = baselineOriginY - headerToBoardSpacing;
    }

    void FitCamera()
    {
        if (Camera.main == null || !Camera.main.orthographic) return;

        // Dark background so Canvas AppBackground shows the correct color
        Camera.main.backgroundColor = new Color(0.059f, 0.059f, 0.071f);

        float centerX = origin.x + (columns - 1) * cellSize * 0.5f;
        float gridTop = origin.y + (rows - 1) * cellSize + cellSize * 0.5f;

        // Use actual tray Y from TrayManager (set in Awake, available by Start)
        float trayY = (TrayManager.Instance != null && TrayManager.Instance.slotPositions.Length > 0)
                      ? TrayManager.Instance.slotPositions[0].y
                      : origin.y - 2.4f * cellSize;  // fallback matches ComputeResponsiveLayout

        float trayBottom = trayY - cellSize;
        float viewBottom = trayBottom - 0.2f;

        // Compute how many world units above the grid the header needs.
        // Canvas is 1080×1920 (match=1), header is 200 canvas-px, gap is 40 canvas-px.
        // Formula: viewTopMargin = baseWorldH * (topSafe+headerH+gapPx) / (canvasH-topSafe-headerH-gapPx)
        // This guarantees the header bottom sits exactly (gapPx) above the grid top,
        // independent of device screen size.
        // Constants MUST match ComputeResponsiveLayout() exactly.
        float baseWorldH    = gridTop - viewBottom;
        const float kCanvasH = 1920f;
        const float kHeaderH = 228f;   // ← must match ComputeResponsiveLayout
        float kGapPx         = ResponsiveLayoutController.HeaderToGridSpacingPx();
        // Account for notch / Dynamic Island (0 on iPhone 8 and notch-free iPads).
        float topSafeInset = ResponsiveLayoutController.TopSafeInsetCanvas();
        float bottomSafeInset = ResponsiveLayoutController.BottomSafeInsetCanvas();
        float reservedCanvas = topSafeInset + bottomSafeInset + kHeaderH + kGapPx;
        float availableCanvas = Mathf.Max(1f, kCanvasH - reservedCanvas);
        float viewTopMargin = baseWorldH * (topSafeInset + kHeaderH + kGapPx) / availableCanvas;
        float viewBottomMargin = baseWorldH * bottomSafeInset / availableCanvas;
        float viewTop       = gridTop + viewTopMargin;
        viewBottom         -= viewBottomMargin;

        float halfHeight = (viewTop - viewBottom) * 0.5f;
        float centerY    = (viewTop + viewBottom) * 0.5f;

        Camera.main.transform.position = new Vector3(centerX, centerY,
                                            Camera.main.transform.position.z);
        Camera.main.orthographicSize = halfHeight;
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    public Vector3 GridToWorld(int col, int row)
    {
        return new Vector3(
            origin.x + col * cellSize,
            origin.y + row * cellSize,
            0f);
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int col = Mathf.RoundToInt((worldPos.x - origin.x) / cellSize);
        int row = Mathf.RoundToInt((worldPos.y - origin.y) / cellSize);
        return new Vector2Int(col, row);
    }

    // API contract for hover-preview systems.
    // Returns false when pointer is outside board bounds.
    public bool TryWorldToCell(Vector2 worldPos, out Vector2Int cell)
    {
        cell = WorldToGrid(new Vector3(worldPos.x, worldPos.y, 0f));
        return IsInBounds(cell.x, cell.y);
    }

    public Vector3 CellToWorld(Vector2Int cell)
        => GridToWorld(cell.x, cell.y);

    // Returns the nearest on-board cell index to the given world point.
    public Vector2Int FindNearestCell(Vector3 worldPosition)
    {
        Vector2Int nearest = WorldToGrid(worldPosition);
        nearest.x = Mathf.Clamp(nearest.x, 0, columns - 1);
        nearest.y = Mathf.Clamp(nearest.y, 0, rows - 1);
        return nearest;
    }

    public bool IsWorldPositionOverGrid(Vector3 worldPos)
    {
        float left   = origin.x - cellSize * 0.5f;
        float right  = origin.x + (columns - 1) * cellSize + cellSize * 0.5f;
        float bottom = origin.y - cellSize * 0.5f;
        float top    = origin.y + (rows - 1) * cellSize + cellSize * 0.5f;
        return worldPos.x >= left && worldPos.x <= right && worldPos.y >= bottom && worldPos.y <= top;
    }

    public bool TryGetTrayBoundsWorld(out Rect bounds)
    {
        bounds = _trayBoundsWorld;
        return _hasTrayBounds;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    public bool IsInBounds(int col, int row)
        => col >= 0 && col < columns && row >= 0 && row < rows;

    public bool IsFree(int col, int row)
        => IsInBounds(col, row) && _grid[col, row] == null;

    public bool CanPlace(Vector2Int anchor, Vector2Int[] offsets)
    {
        foreach (var o in offsets)
            if (!IsFree(anchor.x + o.x, anchor.y + o.y))
                return false;
        return true;
    }

    public bool CanPlaceAt(Vector2Int originCell, Vector2Int[] shape)
        => CanPlace(originCell, shape);

    // Checks every grid cell - used for game-over detection
    public bool CanPlaceAnywhere(Vector2Int[] offsets)
    {
        for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++)
                if (CanPlace(new Vector2Int(c, r), offsets))
                    return true;
        return false;
    }

    // Returns a board snapshot for SmartSpawnController.
    // board[col, row] == true means the cell is occupied.
    // Allocates a new array each call; only call once per refill (from TrayManager).
    public bool[,] GetBoardSnapshot()
    {
        var snap = new bool[columns, rows];
        for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++)
                snap[c, r] = _grid[c, r] != null;
        return snap;
    }

    // ── Placement (coroutine - waits for clear animation) ─────────────────────

    public IEnumerator PlaceBlockRoutine(Vector2Int anchor, Vector2Int[] offsets,
                                         List<GameObject> squareObjects)
    {
        // Snap squares onto the grid with a quick ease-out lerp (0.07 s)
        for (int i = 0; i < offsets.Length; i++)
        {
            int c = anchor.x + offsets[i].x;
            int r = anchor.y + offsets[i].y;
            _grid[c, r] = squareObjects[i];
            StartCoroutine(SnapLerp(squareObjects[i], GridToWorld(c, r), 0.07f));
        }

        // Ses efekti yerleştirme anında çalışsın (lerp tamamlanmasını bekleme)
        AudioManager.Instance?.PlaySfx("place");

        // Placement bounce: scale 1.0 → 1.08 → 1.0 per placed square
        foreach (var sq in squareObjects)
            StartCoroutine(SquareBounce(sq));
        TriggerBoardShake();
        OnBlockPlaced();

        var fullRows = FindFullRows();
        var fullCols = FindFullCols();
        int cleared  = fullRows.Count + fullCols.Count;

        float scoreMultiplier = 1f;

        if (cleared > 0)
        {
            // Kaç hücre temizlendi (kesişimler bir kez sayılır)
            int cellsCleared = fullRows.Count * columns + fullCols.Count * rows
                               - fullRows.Count * fullCols.Count;

            // Tier: 0=normal, 1=Big Bang (9-15), 2=Mega Bang (16-21), 3=Ultra Bang (22+)
            int clearTier = cellsCleared <= 8  ? 0
                          : cellsCleared <= 15 ? 1
                          : cellsCleared <= 21 ? 2 : 3;

            scoreMultiplier = clearTier == 0 ? 1f
                            : clearTier == 1 ? 2f
                            : clearTier == 2 ? 3f : 4f;

            if (clearTier > 0)
                yield return StartCoroutine(BigFlashAndClear(fullRows, fullCols, clearTier));
            else
                yield return StartCoroutine(FlashAndClear(fullRows, fullCols));

            AudioManager.Instance?.PlayClear();
            OnLineCleared(cleared);
            if (cleared > 1)
                OnCombo();
        }

        // Inform SmartSpawnController so it can track line-clear drought for mercy mode.
        SmartSpawnController.NotifyLinesCleared(cleared);

        GameManager.Instance?.AddScore(offsets.Length, cleared, scoreMultiplier);
    }

    // Visual polish hook:
    // Called immediately after a block is committed to the board.
    // Use this for haptics / placement sound layering.
    void OnBlockPlaced()
    {
        // TODO: Add custom placement haptic + layered placement SFX here.
    }

    // Visual polish hook:
    // Called when at least one row/column is cleared in a move.
    // Use this for clear particles and screen accents.
    void OnLineCleared(int linesCleared)
    {
        // TODO: Spawn line-clear particles, glow trails, etc.
    }

    // Visual polish hook:
    // Called on multi-line clear in one move.
    // Use this for "Mega Bang" UI feedback and combo-specific effects.
    void OnCombo()
    {
        // TODO: Trigger combo UI banner, extra shake pulse, and combo SFX.
    }

    // ── Line detection ────────────────────────────────────────────────────────

    List<int> FindFullRows()
    {
        var full = new List<int>();
        for (int r = 0; r < rows; r++)
        {
            bool isFull = true;
            for (int c = 0; c < columns; c++)
                if (_grid[c, r] == null) { isFull = false; break; }
            if (isFull) full.Add(r);
        }
        return full;
    }

    List<int> FindFullCols()
    {
        var full = new List<int>();
        for (int c = 0; c < columns; c++)
        {
            bool isFull = true;
            for (int r = 0; r < rows; r++)
                if (_grid[c, r] == null) { isFull = false; break; }
            if (isFull) full.Add(c);
        }
        return full;
    }

    // ── Line clear animation ──────────────────────────────────────────────────

    IEnumerator FlashAndClear(List<int> fullRows, List<int> fullCols)
    {
        var cells = new HashSet<(int c, int r)>();

        foreach (int r in fullRows)
            for (int c = 0; c < columns; c++)
                cells.Add((c, r));

        foreach (int c in fullCols)
            for (int r = 0; r < rows; r++)
                cells.Add((c, r));

        float interval = 0.08f;

        for (int i = 0; i < 3; i++)
        {
            SetCellColors(cells, Color.white);
            yield return new WaitForSeconds(interval);
            SetCellColors(cells, new Color(0.08f, 0.08f, 0.08f));
            yield return new WaitForSeconds(interval);
        }

        foreach (var (c, r) in cells)
            ClearCell(c, r);
    }

    // Büyük clear: altın sarısı halo + scale sıçraması + hızlı 5 blink
    // tier: 1=BIG BANG, 2=MEGA BANG, 3=ULTRA BANG
    IEnumerator BigFlashAndClear(List<int> fullRows, List<int> fullCols, int tier = 1)
    {
        var cells = new HashSet<(int c, int r)>();
        foreach (int r in fullRows)
            for (int c = 0; c < columns; c++)
                cells.Add((c, r));
        foreach (int c in fullCols)
            for (int r = 0; r < rows; r++)
                cells.Add((c, r));

        // BIG/MEGA/ULTRA BANG ekranı — UIManager üzerinde çalışır, beklenmez
        UIManager.Instance?.ShowBigClear(tier);

        // Renk sırası: altın → beyaz → altın → neon-beyaz → altın → sil
        Color gold      = new Color(1.00f, 0.85f, 0.10f); // #FFD91A
        Color brightW   = new Color(1.00f, 1.00f, 0.85f); // sıcak beyaz
        Color dimGold   = new Color(0.20f, 0.16f, 0.00f); // koyu (aradaki kararma)

        float flashInterval = 0.055f;  // normal'den daha hızlı (0.08 → 0.055)

        // Hücreleri scale ile fırlat
        foreach (var (c, r) in cells)
        {
            var sq = _grid[c, r];
            if (sq != null) StartCoroutine(BigBounce(sq));
        }

        // 5 kez blink (altın + beyaz + kararma)
        Color[] sequence = { gold, brightW, gold, brightW, gold, dimGold, gold, dimGold, gold, dimGold };
        foreach (var col in sequence)
        {
            SetCellColors(cells, col);
            yield return new WaitForSeconds(flashInterval);
        }

        foreach (var (c, r) in cells)
            ClearCell(c, r);
    }

    // Hücreleri scale 1 → 1.3 → 0 (parça parça dağılır gibi)
    IEnumerator BigBounce(GameObject sq)
    {
        if (sq == null) yield break;
        float baseScale = cellSize * 0.9f;
        float duration  = 0.55f;   // BigFlashAndClear süresi kadar
        float t         = 0f;

        while (t < duration && sq != null)
        {
            t += Time.deltaTime;
            float frac = t / duration;
            // 0→0.4: büyüt (1→1.3), 0.4→1.0: küçült (1.3→0)
            float s = frac < 0.4f
                ? Mathf.Lerp(baseScale, baseScale * 1.30f, frac / 0.4f)
                : Mathf.Lerp(baseScale * 1.30f, 0f, (frac - 0.4f) / 0.6f);
            if (sq != null) sq.transform.localScale = Vector3.one * s;
            yield return null;
        }
    }

    void SetCellColors(HashSet<(int c, int r)> cells, Color color)
    {
        foreach (var (c, r) in cells)
            if (_grid[c, r] != null &&
                _grid[c, r].TryGetComponent<SpriteRenderer>(out var sr))
                sr.color = color;
    }

    void ClearCell(int c, int r)
    {
        if (_grid[c, r] == null) return;
        Destroy(_grid[c, r]);
        _grid[c, r] = null;
    }

    // Hızlı ease-out lerp: sürüklenen karedeki pozisyondan grid hücresine geçiş.
    // Cubic ease-out — başlangıç hızlı, son %20'de yavaşlar (snap hissi).
    IEnumerator SnapLerp(GameObject sq, Vector3 target, float duration)
    {
        if (sq == null) yield break;
        Vector3 start   = sq.transform.position;
        float   elapsed = 0f;

        while (elapsed < duration && sq != null)
        {
            elapsed += Time.deltaTime;
            float t    = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f); // cubic ease-out
            sq.transform.position = Vector3.Lerp(start, target, ease);
            yield return null;
        }

        if (sq != null) sq.transform.position = target;
    }

    // Small pop-scale on each square when a piece lands
    IEnumerator SquareBounce(GameObject sq)
    {
        if (sq == null) yield break;

        float baseScale = cellSize * 0.9f;
        float peak      = baseScale * placeBouncePeak;
        float duration  = placeBounceDuration;
        float t         = 0f;

        // Scale up
        while (t < duration * 0.5f)
        {
            t += Time.deltaTime;
            if (sq == null) yield break;
            float frac = Mathf.Clamp01(t / (duration * 0.5f));
            float ease = 1f - Mathf.Pow(1f - frac, 2f);
            sq.transform.localScale = Vector3.one * Mathf.Lerp(baseScale, peak, ease);
            yield return null;
        }

        // Scale back down
        t = 0f;
        while (t < duration * 0.5f)
        {
            t += Time.deltaTime;
            if (sq == null) yield break;
            float frac = Mathf.Clamp01(t / (duration * 0.5f));
            float ease = 1f - Mathf.Pow(1f - frac, 2f);
            sq.transform.localScale = Vector3.one * Mathf.Lerp(peak, baseScale, ease);
            yield return null;
        }

        if (sq)
            sq.transform.localScale = Vector3.one * baseScale;
    }

    // ── Placement preview ─────────────────────────────────────────────────────

    public void ShowPreview(Vector2Int anchor, Vector2Int[] offsets)
    {
        ShowPreview(anchor, offsets, previewValidColor);
    }

    public void ShowPreview(Vector2Int anchor, Vector2Int[] offsets, Color validPreviewColor)
    {
        bool  valid    = CanPlaceAt(anchor, offsets);
        Color mainCol  = valid ? validPreviewColor : previewInvalidColor;
        // Glow: aynı renk, %20 alpha ile %22 daha büyük — halo efekti
        Color glowCol  = new Color(mainCol.r, mainCol.g, mainCol.b, 0.20f);

        EnsurePreviewPool(offsets.Length);
        int index = 0;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int o = offsets[i];
            int c = anchor.x + o.x;
            int r = anchor.y + o.y;
            if (!IsInBounds(c, r)) continue;

            Vector3 worldPos = CellToWorld(new Vector2Int(c, r));
            var visual = _previewPool[index];
            visual.glowGo.transform.position = worldPos;
            visual.fillGo.transform.position = worldPos;
            visual.glowSr.color = glowCol;
            visual.fillSr.color = mainCol;
            if (!visual.glowGo.activeSelf) visual.glowGo.SetActive(true);
            if (!visual.fillGo.activeSelf) visual.fillGo.SetActive(true);
            _previewPool[index] = visual;
            index++;
        }

        for (int i = index; i < _activePreviewCount; i++)
        {
            if (_previewPool[i].glowGo.activeSelf) _previewPool[i].glowGo.SetActive(false);
            if (_previewPool[i].fillGo.activeSelf) _previewPool[i].fillGo.SetActive(false);
        }

        _activePreviewCount = index;
    }

    public void ClearPreview()
    {
        for (int i = 0; i < _activePreviewCount; i++)
        {
            if (_previewPool[i].glowGo.activeSelf) _previewPool[i].glowGo.SetActive(false);
            if (_previewPool[i].fillGo.activeSelf) _previewPool[i].fillGo.SetActive(false);
        }
        _activePreviewCount = 0;
    }

    void EnsurePreviewPool(int count)
    {
        while (_previewPool.Count < count)
        {
            var glow = new GameObject("PreviewGlow");
            glow.transform.SetParent(_boardVisualRoot, false);
            glow.transform.localScale = Vector3.one * cellSize * 1.22f;
            var glowSr = glow.AddComponent<SpriteRenderer>();
            glowSr.sprite = GetOrCreateSprite();
            glowSr.sortingOrder = -1;
            glow.SetActive(false);

            var fill = new GameObject("PreviewFill");
            fill.transform.SetParent(_boardVisualRoot, false);
            fill.transform.localScale = Vector3.one * cellSize * 0.96f;
            var fillSr = fill.AddComponent<SpriteRenderer>();
            fillSr.sprite = GetOrCreateSprite();
            fillSr.sortingOrder = 0;
            fill.SetActive(false);

            _previewPool.Add(new PreviewCellVisual
            {
                glowGo = glow,
                glowSr = glowSr,
                fillGo = fill,
                fillSr = fillSr
            });
        }
    }

    // ── Board + tray containers ───────────────────────────────────────────────

    // Dark rounded-rect surface that frames the 8×8 cell grid.
    // Renders at sortingOrder -3/-4 so it sits behind the bg cells (-2).
    void BuildBoardContainer()
    {
        float cx = origin.x + (columns - 1) * cellSize * 0.5f;

        // Asymmetric padding: flush on top so the board never bleeds toward the header,
        // generous on the bottom and sides so cells feel contained.
        float padSide   = 0.40f;
        float padTop    = 0.02f;  // nearly flush — no grey band above row 7
        float padBottom = 0.40f;

        // Visual grid extents (cell centres ± half cell)
        float gridVisualLeft   = origin.x - cellSize * 0.5f;
        float gridVisualRight  = origin.x + (columns - 1) * cellSize + cellSize * 0.5f;
        float gridVisualBottom = origin.y - cellSize * 0.5f;
        float gridVisualTop    = origin.y + (rows - 1) * cellSize + cellSize * 0.5f;

        float boardLeft   = gridVisualLeft   - padSide;
        float boardRight  = gridVisualRight  + padSide;
        float boardBottom = gridVisualBottom - padBottom;
        float boardTop    = gridVisualTop    + padTop;

        float boardW = boardRight - boardLeft;
        float boardH = boardTop   - boardBottom;
        float cy     = (boardBottom + boardTop) * 0.5f;

        // Drop shadow — slightly larger, offset down-right
        SpawnPanel("BoardShadow",
            pos:   new Vector3(cx + 0.12f, cy - 0.18f, 0f),
            scale: new Vector2(boardW + 0.40f, boardH + 0.40f),
            color: new Color(0f, 0f, 0f, 0.50f),
            order: -5,
            parent: _boardContainerRoot);

        // Board surface slightly lighter than app bg so the grid feels seated.
        SpawnPanel("BoardSurface",
            pos:   new Vector3(cx, cy, 0f),
            scale: new Vector2(boardW, boardH),
            color: new Color(0.074f, 0.074f, 0.090f, 1f),
            order: -4,
            parent: _boardContainerRoot);

        // Soft inset to fake a rounded tray/board card interior.
        SpawnPanel("BoardInset",
            pos:   new Vector3(cx, cy - 0.03f * cellSize, 0f),
            scale: new Vector2(boardW - 0.18f, boardH - 0.18f),
            color: new Color(0f, 0f, 0f, 0.12f),
            order: -3,
            parent: _boardContainerRoot);
    }

    // Rounded panel behind the 3 tray pieces.
    // Called from Start() so TrayManager.slotPositions is already populated.
    void BuildTrayContainer()
    {
        if (TrayManager.Instance == null) return;
        var   slots      = TrayManager.Instance.slotPositions;
        float pieceScale = TrayManager.Instance.trayBlockScale;
        if (slots.Length == 0) return;

        float trayY   = slots[0].y;
        float leftX   = slots[0].x;
        float rightX  = slots[slots.Length - 1].x;
        float centerX = (leftX + rightX) * 0.5f;

        float hPad  = cellSize * pieceScale * 1.1f;
        float trayW = (rightX - leftX) + hPad * 2f;

        // Height covers a 2-cell block comfortably — 3-cell pieces stick out above, which is fine.
        // Do NOT try to cover the full 3-cell height; that would push the top into grid territory.
        float trayH   = cellSize * pieceScale * 2.2f;
        float centerY = trayY; // centred on the slot anchor; do NOT shift upward

        // Hard clamp: tray panel top must stay below the board container's bottom edge.
        // boardBottom = gridVisualBottom (origin.y - cellSize*0.5) - padBottom (0.40)
        float boardBottom = origin.y - cellSize * 0.5f - 0.40f;
        float minGap      = 0.25f;
        float maxTop      = boardBottom - minGap;
        if (centerY + trayH * 0.5f > maxTop)
            centerY = maxTop - trayH * 0.5f;

        SpawnPanel("TrayShadow",
            pos:   new Vector3(centerX + 0.10f, centerY - 0.12f, 0f),
            scale: new Vector2(trayW + 0.40f, trayH + 0.20f),
            color: new Color(0f, 0f, 0f, 0.45f),
            order: -5,
            parent: _trayContainerRoot);

        GameObject trayPanel = SpawnPanel("TrayPanel",
            pos:   new Vector3(centerX, centerY, 0f),
            scale: new Vector2(trayW, trayH),
            color: new Color(0.074f, 0.074f, 0.090f, 1f),
            order: -4,
            parent: _trayContainerRoot);

        SpawnPanel("TrayInset",
            pos:   new Vector3(centerX, centerY - 0.03f * cellSize, 0f),
            scale: new Vector2(trayW - 0.16f, trayH - 0.16f),
            color: new Color(0f, 0f, 0f, 0.12f),
            order: -3,
            parent: _trayContainerRoot);

        _trayBoundsWorld = new Rect(
            centerX - trayW * 0.5f,
            centerY - trayH * 0.5f,
            trayW,
            trayH);
        _hasTrayBounds = true;

        var breather = trayPanel.GetComponent<TrayContainerBreath>();
        if (breather == null) breather = trayPanel.AddComponent<TrayContainerBreath>();
        breather.scaleAmplitude = 0.012f;
        breather.cycleSeconds = 2.6f;
    }

    // Spawns a world-space rounded-rectangle sprite.
    GameObject SpawnPanel(string name, Vector3 pos, Vector2 scale, Color color, int order, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position   = pos;
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);

        var sr          = go.AddComponent<SpriteRenderer>();
        sr.sprite       = GetOrCreateSprite();
        sr.color        = color;
        sr.sortingOrder = order;
        return go;
    }

    // ── Background visual ─────────────────────────────────────────────────────

    void BuildBackground()
    {
        var container = new GameObject("BackgroundCells");
        container.transform.SetParent(_boardVisualRoot, false);

        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                var cell = new GameObject($"BG_{c}_{r}");
                cell.transform.parent     = container.transform;
                cell.transform.position   = GridToWorld(c, r);
                cell.transform.localScale = Vector3.one * cellSize * 0.96f;

                var sr          = cell.AddComponent<SpriteRenderer>();
                sr.sprite       = GetOrCreateSprite();
                sr.color        = emptyCellColor;
                sr.sortingOrder = -2;
            }
        }
    }

    void TriggerBoardShake()
    {
        if (_boardShakeRoutine != null) StopCoroutine(_boardShakeRoutine);
        _boardShakeRoutine = StartCoroutine(BoardShakeRoutine());
    }

    IEnumerator BoardShakeRoutine()
    {
        if (_boardVisualRoot == null) yield break;

        Vector3 basePos = _boardVisualRoot.localPosition;
        float duration  = boardShakeDuration;
        float amp       = boardShakeAmplitudeCells * cellSize;
        float elapsed   = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float decay = 1f - (elapsed / duration);
            Vector2 jitter = Random.insideUnitCircle * (amp * decay);
            _boardVisualRoot.localPosition = basePos + new Vector3(jitter.x, jitter.y, 0f);
            yield return null;
        }

        _boardVisualRoot.localPosition = basePos;
        _boardShakeRoutine = null;
    }

    // ── Sprite factory ────────────────────────────────────────────────────────

    // Public so BlockPiece depth layers can reuse the same shared sprite.
    public static Sprite GetOrCreateSprite()
    {
        if (_cachedSprite == null)
            _cachedSprite = MakeSquareSprite();
        return _cachedSprite;
    }

    // 32×32 rounded-rectangle sprite with anti-aliased corners.
    // FilterMode.Bilinear + corner radius gives soft casual-plastic look.
    // Replaces the old 4×4 Point-filtered flat square.
    public static Sprite MakeSquareSprite()
    {
        const int S = 32;   // texture resolution
        const int R = 5;    // corner radius in pixels

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        var pixels = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // For each corner quadrant, find the nearest corner-circle centre.
                int cx = (x < R) ? R : (x >= S - R ? S - 1 - R : x);
                int cy = (y < R) ? R : (y >= S - R ? S - 1 - R : y);

                float dist  = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                // +0.5 offset gives one pixel of anti-aliased feathering.
                float alpha = Mathf.Clamp01(R - dist + 0.5f);

                pixels[y * S + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), (float)S);
    }
}
