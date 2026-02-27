using UnityEngine;

// Lightweight layout helpers for portrait mobile UI sizing.
public static class ResponsiveLayoutController
{
    public const float CanvasReferenceHeight = 1920f;

    public static float ToCanvasPx(float screenPx)
    {
        if (Screen.height <= 0) return 0f;
        return screenPx * CanvasReferenceHeight / Screen.height;
    }

    public static float TopSafeInsetCanvas()
    {
        return ToCanvasPx(Screen.height - Screen.safeArea.yMax);
    }

    public static float BottomSafeInsetCanvas()
    {
        return ToCanvasPx(Screen.safeArea.yMin);
    }

    public static float TrayHeightPx()
    {
        float px = Mathf.Clamp(Screen.height * 0.25f, 180f, 320f);
        return ToCanvasPx(px);
    }

    public static float GridToTraySpacingPx()
    {
        float px = Mathf.Clamp(Screen.height * 0.04f, 16f, 48f);
        return ToCanvasPx(px);
    }

    public static float HeaderToGridSpacingPx()
    {
        float px = Mathf.Clamp(Screen.height * 0.02f, 16f, 28f);
        return ToCanvasPx(px);
    }

    public static float HeaderTopPaddingPx()
    {
        float px = Mathf.Clamp(Screen.height * 0.025f, 18f, 34f);
        return ToCanvasPx(px);
    }

    public static float HeaderHeightPx()
    {
        float px = Mathf.Clamp(Screen.height * 0.12f, 90f, 130f);
        return ToCanvasPx(px);
    }
}
