using UnityEngine;

// Minimal tray fit helper: scales down (never up) and recenters a resting tray piece
// so its full visual footprint stays inside the slot area.
public static class TrayPieceFitter
{
    public enum SlotAlignmentX
    {
        Left,
        Center,
        Right
    }

    // Keeps tray pieces readable while still fitting most shapes.
    const float MinScaleFactor = 0.85f;
    const float DefaultPadding = 0.95f;
    const float ThinLineBoost = 1.15f;

    // pieceRoot: world-space root of BlockPiece
    // slotRectWorld: slot bounds in world units
    // shapeCells: block offsets (Vector2Int[])
    public static void FitToSlot(
        Transform pieceRoot,
        Rect slotRectWorld,
        Vector2Int[] shapeCells,
        int slotIndex,
        float fill = DefaultPadding)
    {
        if (pieceRoot == null || shapeCells == null || shapeCells.Length == 0) return;

        // Assumption from existing piece build:
        // child square localScale.x is 0.9 * cellSize (see BlockPiece.BuildSquares).
        float cellSize = InferCellSize(pieceRoot);
        if (cellSize <= 0.0001f) return;

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        for (int i = 0; i < shapeCells.Length; i++)
        {
            Vector2Int c = shapeCells[i];
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.y > maxY) maxY = c.y;
        }

        float widthCells = maxX - minX + 1;
        float heightCells = maxY - minY + 1;
        float currentScale = pieceRoot.localScale.x;

        float availW = slotRectWorld.width;
        float availH = slotRectWorld.height;

        bool isThinLine = (widthCells >= 4f && Mathf.Approximately(heightCells, 1f)) ||
                          (heightCells >= 4f && Mathf.Approximately(widthCells, 1f));
        float desiredScale = currentScale * (isThinLine ? ThinLineBoost : 1f);

        // Absolute scale cap that guarantees the full footprint fits inside slotRectWorld.
        float maxScaleToFit = Mathf.Min(
            availW / Mathf.Max(widthCells * cellSize, 0.0001f),
            availH / Mathf.Max(heightCells * cellSize, 0.0001f));
        maxScaleToFit *= Mathf.Clamp(fill, 0.3f, 1f);

        float floorScale = currentScale * MinScaleFactor;
        float finalScale = Mathf.Min(desiredScale, maxScaleToFit);
        // Preserve readability floor only when slot has enough room.
        if (maxScaleToFit >= floorScale)
            finalScale = Mathf.Max(finalScale, floorScale);

        // Keep perfect centering: if any previous tuning would overflow, reduce scale instead of shifting.
        float scaledWidth = widthCells * cellSize * finalScale;
        float scaledHeight = heightCells * cellSize * finalScale;
        if (scaledWidth > slotRectWorld.width || scaledHeight > slotRectWorld.height)
        {
            float down = Mathf.Min(
                slotRectWorld.width / Mathf.Max(scaledWidth, 0.0001f),
                slotRectWorld.height / Mathf.Max(scaledHeight, 0.0001f));
            finalScale *= down;
        }

        pieceRoot.localScale = Vector3.one * finalScale;

        // Pivot assumption:
        // piece root pivot is effectively local origin (0,0) in world-space Transform,
        // squares are positioned by cell offsets around this origin.
        // We center by shape bounds center in local space.
        float scale = pieceRoot.localScale.x;
        float minLocalX = (minX - 0.5f) * cellSize * scale;
        float maxLocalX = (maxX + 0.5f) * cellSize * scale;
        float minLocalY = (minY - 0.5f) * cellSize * scale;
        float maxLocalY = (maxY + 0.5f) * cellSize * scale;
        float widthWorld = maxLocalX - minLocalX;
        float heightWorld = maxLocalY - minLocalY;

        SlotAlignmentX alignX = ResolveAlignment(slotIndex);
        float laneInset = Mathf.Max(0f, slotRectWorld.width - widthWorld) * 0.5f;

        float desiredMinX = slotRectWorld.xMin;
        if (alignX == SlotAlignmentX.Center)
            desiredMinX = slotRectWorld.center.x - widthWorld * 0.5f;
        else if (alignX == SlotAlignmentX.Left)
            desiredMinX = slotRectWorld.xMin + laneInset;
        else if (alignX == SlotAlignmentX.Right)
            desiredMinX = slotRectWorld.xMax - laneInset - widthWorld;

        float desiredMinY = slotRectWorld.center.y - heightWorld * 0.5f;

        pieceRoot.position = new Vector3(
            desiredMinX - minLocalX,
            desiredMinY - minLocalY,
            pieceRoot.position.z);

        // No post-position side clamp here: shape should remain visually centered in its slot.
    }

    static float InferCellSize(Transform pieceRoot)
    {
        float maxSq = 0f;
        for (int i = 0; i < pieceRoot.childCount; i++)
        {
            Transform ch = pieceRoot.GetChild(i);
            if (!ch.name.StartsWith("Sq_")) continue;
            float s = Mathf.Abs(ch.localScale.x);
            if (s > maxSq) maxSq = s;
        }

        if (maxSq > 0.0001f)
            return maxSq / 0.9f;

        return GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
    }

    static SlotAlignmentX ResolveAlignment(int slotIndex)
    {
        if (slotIndex <= 0) return SlotAlignmentX.Left;
        if (slotIndex >= 2) return SlotAlignmentX.Right;
        return SlotAlignmentX.Center;
    }

}
