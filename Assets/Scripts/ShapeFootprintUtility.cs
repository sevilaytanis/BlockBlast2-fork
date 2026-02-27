using UnityEngine;

// Shape footprint helpers for slot fitting and safe tray clamping.
public static class ShapeFootprintUtility
{
    public struct BoundsInt2D
    {
        public int minX;
        public int maxX;
        public int minY;
        public int maxY;
    }

    public static BoundsInt2D CalculateBounds(Vector2Int[] cells)
    {
        BoundsInt2D b = new BoundsInt2D
        {
            minX = int.MaxValue,
            maxX = int.MinValue,
            minY = int.MaxValue,
            maxY = int.MinValue
        };

        for (int i = 0; i < cells.Length; i++)
        {
            Vector2Int c = cells[i];
            if (c.x < b.minX) b.minX = c.x;
            if (c.x > b.maxX) b.maxX = c.x;
            if (c.y < b.minY) b.minY = c.y;
            if (c.y > b.maxY) b.maxY = c.y;
        }

        return b;
    }

    public static Vector2 WorldSpan(Vector2Int[] cells, float cellSize)
    {
        BoundsInt2D b = CalculateBounds(cells);
        float w = (b.maxX - b.minX + 1) * cellSize;
        float h = (b.maxY - b.minY + 1) * cellSize;
        return new Vector2(w, h);
    }

    // Fits a shape into a slot world-size while preserving aspect ratio.
    public static float ComputeFitScale(
        Vector2 slotWorldSize,
        Vector2Int[] cells,
        float cellSize,
        float maxScale,
        float fill = 0.88f)
    {
        Vector2 span = WorldSpan(cells, cellSize);
        if (span.x <= 0.0001f || span.y <= 0.0001f) return maxScale;

        float fitX = (slotWorldSize.x * fill) / span.x;
        float fitY = (slotWorldSize.y * fill) / span.y;
        float fit = Mathf.Min(fitX, fitY);
        return Mathf.Clamp(fit, 0.28f, maxScale);
    }

    // UI variant requested for RectTransform-based trays.
    public static void FitToSlot(
        RectTransform pieceRoot,
        RectTransform slotRect,
        Vector2Int[] cells,
        float cellSize = 1f,
        float fill = 0.88f)
    {
        if (pieceRoot == null || slotRect == null || cells == null || cells.Length == 0) return;

        Vector2 span = WorldSpan(cells, cellSize);
        Rect slot = slotRect.rect;
        float fitX = (slot.width * fill) / Mathf.Max(span.x, 0.0001f);
        float fitY = (slot.height * fill) / Mathf.Max(span.y, 0.0001f);
        float fit = Mathf.Max(0.01f, Mathf.Min(fitX, fitY));

        pieceRoot.localScale = Vector3.one * fit;
        pieceRoot.anchoredPosition = Vector2.zero;
    }
}
