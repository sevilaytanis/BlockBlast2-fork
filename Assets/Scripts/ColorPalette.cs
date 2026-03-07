using UnityEngine;

// Merkezi renk kataloğu — tüm renk referansları buradan alınır.
// Renkler önce Resources/BlockPalette.asset'ten yüklenir (BlockPaletteData ScriptableObject).
// Asset bulunamazsa aşağıdaki _fallbackColors kullanılır.
// Asset panelinden düzenlemek için: Assets → Resources → BlockPalette
//
public static class ColorPalette
{
    // ── ScriptableObject palette yükleme ─────────────────────────────────────
    private static BlockPaletteData _paletteData;

    public static BlockPaletteData PaletteData
    {
        get
        {
            if (_paletteData == null)
                _paletteData = Resources.Load<BlockPaletteData>("BlockPalette");
            return _paletteData;
        }
    }

    // Fallback — BlockPalette.asset bulunamazsa kullanılır.
    private static readonly Color[] _fallbackColors = new Color[]
    {
        new Color(0.60f, 0.10f, 1.00f, 1f), // Neon Violet
        new Color(0.10f, 0.88f, 0.20f, 1f), // Bright Green
        new Color(0.12f, 0.44f, 1.00f, 1f), // Deep Blue
        new Color(1.00f, 0.48f, 0.04f, 1f), // Bright Orange
        new Color(1.00f, 0.12f, 0.18f, 1f), // Fire Red
        new Color(1.00f, 0.22f, 0.76f, 1f), // Hot Pink
        new Color(0.72f, 1.00f, 0.04f, 1f), // Neon Lime
    };

    // ── Blok renk paleti ─────────────────────────────────────────────────────
    // TrayManager / BlockShapes tarafından kullanılır.
    // Renkleri değiştirmek için Unity editöründe Assets/Resources/BlockPalette assetini aç.

    public static Color[] Blocks
    {
        get
        {
            var p = PaletteData;
            return (p != null && p.blockColors != null && p.blockColors.Length > 0)
                ? p.blockColors
                : _fallbackColors;
        }
    }

    // ── Board / UI renkleri ──────────────────────────────────────────────────

    // Uygulama arka planı ve board yüzeyi — mat koyu lacivert
    public static readonly Color AppBackground  = new Color(0.059f, 0.059f, 0.071f, 1f);   // #0F0F12

    // Boş hücre: board surface'ten fark edilebilir ama blok renklerinin altında kalmalı.
    // Önceki değer (0.11, 0.11, 0.149) bloklar kadar "aktif" görünüyordu.
    public static readonly Color EmptyCell      = new Color(0.082f, 0.082f, 0.102f, 1f);   // #151519

    // Preview — geçerli yerleşim (yeşil-mint, biraz daha yoğun alpha)
    public static readonly Color PreviewValid   = new Color(0.000f, 0.784f, 0.588f, 0.55f); // Mint %55

    // Preview — geçersiz yerleşim (kırmızı-coral)
    public static readonly Color PreviewInvalid = new Color(1.000f, 0.322f, 0.322f, 0.45f); // Coral %45

    // Popup / BIG BANG altın sarısı
    public static readonly Color Gold           = new Color(1.000f, 0.850f, 0.100f, 1f);   // #FFD91A

    // Score popup sarısı
    public static readonly Color ScorePopup     = new Color(1.000f, 0.920f, 0.230f, 1f);   // #FFEB3B

    // ── Yardımcı ─────────────────────────────────────────────────────────────

    public static Color GetRandomBlock()
    {
        var blocks = Blocks;
        return blocks[Random.Range(0, blocks.Length)];
    }

    // Rengi tray ölçeğine (0–1) göre doygunluk artırır — küçük parçalarda renk soluk görünüyor.
    // s=1: değişmez, s=1.15: %15 daha canlı (HSV doygunluğunu klamp ederek).
    public static Color Boost(Color c, float s = 1.15f)
    {
        Color.RGBToHSV(c, out float h, out float sat, out float v);
        sat = Mathf.Clamp01(sat * s);
        return Color.HSVToRGB(h, sat, v);
    }
}
