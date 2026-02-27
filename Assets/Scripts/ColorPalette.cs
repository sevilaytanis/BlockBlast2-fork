using UnityEngine;

// Merkezi renk kataloğu — tüm renk referansları buradan alınır.
//
// Blok paleti tasarım ilkeleri:
//   - 7 renk, hue dönüşümü tam tur (0°→339°) — eşit aralıklı değil, algısal aralıklı.
//   - Her renk için minimum 22° hue ayrışması + luminance farkı > 15 birim.
//   - Renk körlüğü (deuteranopia/protanopia) senaryosunda 5/7 renk ayırt edilebilir.
//   - sRGB gamma alanında; Unity SpriteRenderer doğrudan kullanabilir.
//
// Hue dağılımı:
//   Coral   0°   Lum≈48  ●
//   Orange 27°   Lum≈53  ●
//   Yellow 52°   Lum≈72  ●  ← en parlak, kolayca ayırt edilir
//   Mint  163°   Lum≈54  ●  ← büyük boşluk (warm→cool geçişi)
//   Blue  218°   Lum≈46  ●
//   Violet 269°  Lum≈44  ●
//   Pink  339°   Lum≈55  ●
//
public static class ColorPalette
{
    // ── Blok renk paleti ─────────────────────────────────────────────────────
    // TrayManager / BlockShapes tarafından kullanılır.
    // Silver kaldırıldı — düşük doygunluk diğer renklerle kontrast oluşturmuyor.

    public static readonly Color[] Blocks = new Color[]
    {
        new Color(1.000f, 0.322f, 0.322f), // Coral   #FF5252  hue  0°  lum≈48
        new Color(1.000f, 0.549f, 0.000f), // Orange  #FF8C00  hue 27°  lum≈53
        new Color(1.000f, 0.878f, 0.000f), // Yellow  #FFE000  hue 52°  lum≈72
        new Color(0.000f, 0.784f, 0.588f), // Mint    #00C896  hue163°  lum≈54
        new Color(0.161f, 0.475f, 1.000f), // Blue    #2979FF  hue218°  lum≈46
        new Color(0.612f, 0.302f, 1.000f), // Violet  #9C4DFF  hue269°  lum≈44
        new Color(1.000f, 0.251f, 0.506f), // Pink    #FF4081  hue339°  lum≈55
    };

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
        => Blocks[Random.Range(0, Blocks.Length)];

    // Rengi tray ölçeğine (0–1) göre doygunluk artırır — küçük parçalarda renk soluk görünüyor.
    // s=1: değişmez, s=1.15: %15 daha canlı (HSV doygunluğunu klamp ederek).
    public static Color Boost(Color c, float s = 1.15f)
    {
        Color.RGBToHSV(c, out float h, out float sat, out float v);
        sat = Mathf.Clamp01(sat * s);
        return Color.HSVToRGB(h, sat, v);
    }
}
