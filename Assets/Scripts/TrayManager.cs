using UnityEngine;

// Manages the 3 block slots shown below the board.
// Attach to an empty GameObject named "TrayManager".
//
// Spawn flow (SmartSpawnController — single-pass, fit-aware):
//   1. SmartSpawnController.GenerateBatch() — weighted + fit-scored + mercy-aware
//   2. SpawnInSlot() × 3                   — create GameObjects from final shapes
public class TrayManager : MonoBehaviour
{
    public static TrayManager Instance { get; private set; }

    [Header("Tray Slot World Positions")]
    public Vector3[] slotPositions = new Vector3[]
    {
        new Vector3(-2.8f, -4.5f, 0f),
        new Vector3( 0f,   -4.5f, 0f),
        new Vector3( 2.8f, -4.5f, 0f),
    };

    [Header("Tray Block Display Scale")]
    public float trayBlockScale = 0.72f;

    [Header("Slot Fit")]
    [SerializeField] private float slotWidthFill = 0.86f;
    [SerializeField] private float slotHeightFill = 0.86f;
    [SerializeField, Range(1f, 1.4f)] private float traySlotSpacingMultiplier = 1.35f;
    [SerializeField] private TrayAlignToGrid trayAlignToGrid;

    private BlockPiece[] _slots = new BlockPiece[3];

    void Awake()
    {
        Instance = this;
        if (trayAlignToGrid == null)
            trayAlignToGrid = GetComponent<TrayAlignToGrid>();
        if (trayAlignToGrid == null)
            trayAlignToGrid = gameObject.AddComponent<TrayAlignToGrid>();
    }

    void Start()
    {
        SmartSpawnController.Reset();
        bool alignedToGrid = trayAlignToGrid != null && trayAlignToGrid.AlignNow(this, GridManager.Instance);
        if (!alignedToGrid)
            ApplySlotSpacing();
        RefillTray();
        CheckGameOver();
    }

    // ── Called by BlockPiece on successful board placement ────────────────────

    public void OnBlockPlaced(int slotIndex)
    {
        _slots[slotIndex] = null;

        bool anyLeft = false;
        for (int i = 0; i < 3; i++)
            if (_slots[i] != null) { anyLeft = true; break; }

        // Refill only once ALL three blocks have been placed (Block Blast rule)
        if (!anyLeft)
            RefillTray();

        CheckGameOver();
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    // SmartSpawnController handles everything in one call:
    //   • Weighted 3/4/5-cell selection (score-tiered)
    //   • Last-N history cooldown (no boring repeats)
    //   • Fit-aware candidate scoring (board-state aware)
    //   • Mercy mode (crowded board / line-clear drought)
    //   • ≥1 fit guarantee with retry + fallback rescue
    void RefillTray()
    {
        int score = GameManager.Instance != null ? GameManager.Instance.Score : 0;
        var board = GridManager.Instance.GetBoardSnapshot();
        var shapes = SmartSpawnController.GenerateBatch(3, board, score);

        for (int i = 0; i < 3; i++)
            SpawnInSlot(i, shapes[i]);
    }

    void SpawnInSlot(int slotIndex, Vector2Int[] offsets)
    {
        var go    = new GameObject($"Block_Slot{slotIndex}");
        var piece = go.AddComponent<BlockPiece>();
        piece.Initialize(
            offsets:    offsets,
            color:      BlockShapes.GetRandomColor(),
            trayPos:    slotPositions[slotIndex],
            slotIndex:  slotIndex,
            trayScale:  trayBlockScale
        );
        if (TryGetSlotRectWorld(slotIndex, out Rect slotRect))
            piece.FitToTraySlot(slotRect);
        _slots[slotIndex] = piece;
    }

    // ── Game-over check ───────────────────────────────────────────────────────

    void CheckGameOver()
    {
        // If any remaining piece can fit somewhere on the board, game continues
        foreach (var slot in _slots)
        {
            if (slot == null) continue;
            if (GridManager.Instance.CanPlaceAnywhere(slot.Offsets))
                return;
        }

        GameManager.Instance?.TriggerGameOver();
    }

    // World-space slot rect used for shape fit (spawn) and tray clamping.
    public bool TryGetSlotRectWorld(int slotIndex, out Rect slotRect)
    {
        slotRect = default;
        if (slotIndex < 0 || slotIndex >= slotPositions.Length) return false;
        if (GridManager.Instance == null) return false;
        if (!GridManager.Instance.TryGetTrayBoundsWorld(out Rect tray)) return false;

        float slotW = (tray.width / 3f) * slotWidthFill;
        float neighborDist = NearestNeighborXDistance(slotIndex);
        if (neighborDist > 0.001f)
            slotW = Mathf.Min(slotW, neighborDist * 0.82f);
        float slotH = tray.height * slotHeightFill;
        Vector3 c   = slotPositions[slotIndex];

        slotRect = new Rect(
            c.x - slotW * 0.5f,
            c.y - slotH * 0.5f,
            slotW,
            slotH);
        return true;
    }

    void ApplySlotSpacing()
    {
        if (slotPositions == null || slotPositions.Length == 0) return;
        float centerX = 0f;
        for (int i = 0; i < slotPositions.Length; i++) centerX += slotPositions[i].x;
        centerX /= slotPositions.Length;

        for (int i = 0; i < slotPositions.Length; i++)
        {
            Vector3 p = slotPositions[i];
            p.x = centerX + (p.x - centerX) * traySlotSpacingMultiplier;
            slotPositions[i] = p;
        }
    }

    float NearestNeighborXDistance(int slotIndex)
    {
        if (slotPositions == null || slotPositions.Length < 2) return 0f;
        float best = float.MaxValue;
        float x = slotPositions[slotIndex].x;
        for (int i = 0; i < slotPositions.Length; i++)
        {
            if (i == slotIndex) continue;
            float d = Mathf.Abs(slotPositions[i].x - x);
            if (d < best) best = d;
        }
        return best == float.MaxValue ? 0f : best;
    }
}
