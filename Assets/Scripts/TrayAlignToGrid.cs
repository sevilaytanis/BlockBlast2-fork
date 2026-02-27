using UnityEngine;

// Aligns tray slot X lanes to grid left/center/right so long shapes feel stable.
// Layout-only helper: does not touch gameplay rules.
public class TrayAlignToGrid : MonoBehaviour
{
    [Header("Optional UI refs (if you have UGUI grid/tray rects)")]
    [SerializeField] private RectTransform gridRect;
    [SerializeField] private RectTransform trayRect;
    [SerializeField] private RectTransform[] slots;

    [Header("Lane Padding")]
    [SerializeField] private float sidePaddingPx = 16f;
    [SerializeField] private bool useHalfCellPadding = true;

    [Header("Compact Slot Spacing")]
    [SerializeField] private bool useCompactLanes = true;
    [SerializeField, Range(0.14f, 0.26f)] private float compactSpanScreenRatio = 0.18f;
    [SerializeField] private float compactSpanMinPx = 110f;
    [SerializeField] private float compactSpanMaxPx = 180f;

    // Called by TrayManager before spawning tray pieces.
    public bool AlignNow(TrayManager trayManager, GridManager gridManager)
    {
        if (trayManager == null || gridManager == null || trayManager.slotPositions == null || trayManager.slotPositions.Length < 3)
            return false;

        float leftX;
        float rightX;

        if (!TryGetGridBoundsFromRect(out leftX, out rightX))
        {
            // World-space fallback for this project architecture.
            leftX = gridManager.origin.x - gridManager.cellSize * 0.5f;
            rightX = gridManager.origin.x + (gridManager.columns - 1) * gridManager.cellSize + gridManager.cellSize * 0.5f;
        }

        float pad = useHalfCellPadding
            ? gridManager.cellSize * 0.5f
            : PixelToWorldX(sidePaddingPx);

        float laneCenter = (leftX + rightX) * 0.5f;
        float laneLeft;
        float laneRight;

        if (useCompactLanes)
        {
            float compactPx = Mathf.Clamp(Screen.width * compactSpanScreenRatio, compactSpanMinPx, compactSpanMaxPx);
            float compactSpanWorld = PixelToWorldX(compactPx);

            // Keep enough center-to-center distance for long pieces, but avoid over-spreading.
            float neededWidth = gridManager.cellSize * 5f * trayManager.trayBlockScale * 0.68f
                                + gridManager.cellSize * 0.10f;
            float maxSpanByGrid = Mathf.Max(0.01f, ((rightX - leftX) * 0.5f) - pad);
            float span = Mathf.Clamp(Mathf.Max(compactSpanWorld, neededWidth), 0.01f, maxSpanByGrid);

            laneLeft = laneCenter - span;
            laneRight = laneCenter + span;
        }
        else
        {
            laneLeft = leftX + pad;
            laneRight = rightX - pad;
        }

        // Keep current Y/Z values intact.
        Vector3 s0 = trayManager.slotPositions[0];
        Vector3 s1 = trayManager.slotPositions[1];
        Vector3 s2 = trayManager.slotPositions[2];
        trayManager.slotPositions[0] = new Vector3(laneLeft, s0.y, s0.z);
        trayManager.slotPositions[1] = new Vector3(laneCenter, s1.y, s1.z);
        trayManager.slotPositions[2] = new Vector3(laneRight, s2.y, s2.z);

        return true;
    }

    bool TryGetGridBoundsFromRect(out float leftX, out float rightX)
    {
        leftX = 0f;
        rightX = 0f;
        if (gridRect == null || Camera.main == null) return false;

        var corners = new Vector3[4];
        gridRect.GetWorldCorners(corners);
        leftX = corners[0].x;
        rightX = corners[3].x;
        return rightX > leftX;
    }

    static float PixelToWorldX(float px)
    {
        if (Camera.main == null || Screen.width <= 0) return 0f;
        float worldWidth = Camera.main.orthographicSize * 2f * Camera.main.aspect;
        return worldWidth * (px / Screen.width);
    }
}
