using UnityEngine;

// Grid / Board görsel ayarları — Asset panelinden düzenlenebilir.
// Oluşturmak için: Assets → Create → BlockBlast → Grid Settings
// Runtime'da Resources/GridSettings.asset olarak yüklenir.
//
// Yeni değerleri mevcut asset'e uygulamak için:
//   Unity menüsü → BlockBlast → Reset Visual Assets to Defaults
//
// Renk kodları referans (hex → Unity sRGB):
//   #0A0E25  →  (0.039, 0.055, 0.145)  — board arka plan, siyaha yakın lacivert
//   #050616  →  (0.020, 0.024, 0.086)  — hücre iç gölge, board'dan koyu → recessed
//   ızgara çizgisi → board yüzey rengiyle gap bırakarak görünür (hayalet çizgi)

[CreateAssetMenu(fileName = "GridSettings", menuName = "BlockBlast/Grid Settings", order = 2)]
public class GridSettings : ScriptableObject
{
    [Header("Board Container")]
    [Tooltip("Ana board yüzey rengi — #0A0E25 siyaha yakın lacivert.")]
    public Color boardSurface     = new Color(0.039f, 0.055f, 0.145f, 1.00f); // #0A0E25

    [Tooltip("Board drop shadow.")]
    public Color boardShadow      = new Color(0.008f, 0.012f, 0.035f, 0.80f);

    [Tooltip("Board iç inset gölgesi.")]
    public Color boardInset       = new Color(0f,     0f,     0f,     0.28f);

    [Header("Empty Cell — Recessed Pocket Effect")]
    [Tooltip(
        "Hücre dış kenar — bu hayalet çizgi sadece grid yapısını gösterir.\n" +
        "Alpha 0.06-0.08 önerilir: 'buradayım' demez, rehberlik eder.")]
    public Color cellEdge         = new Color(0.20f, 0.23f, 0.45f, 0.07f);

    [Tooltip(
        "Hücre iç dolgu — board'dan KOYU olması 'recessed pocket' hissi verir.\n" +
        "Bloklar bu karanlık yuvaya yerleşiyor gibi görünür.")]
    public Color cellCenter       = new Color(0.018f, 0.022f, 0.060f, 0.92f); // #050616 siyaha yakın

    [Range(0f, 0.15f)]
    [Tooltip("Sol-üst köşe beyaz highlight. 0 = kapalı, 0.04 = çok hafif cam parlaması.")]
    public float cellHighlightAlpha = 0.00f;

    [Header("Tray Container")]
    [Tooltip("Tray panel rengi — board ile aynı siyaha yakın lacivert önerilir.")]
    public Color trayPanel        = new Color(0.039f, 0.055f, 0.145f, 1.00f); // #0A0E25

    [Tooltip("Tray iç inset.")]
    public Color trayInset        = new Color(0f,     0f,     0f,     0.28f);

    [Tooltip("Tray drop shadow.")]
    public Color trayShadow       = new Color(0f,     0f,     0f,     0.60f);

    [Header("Camera")]
    [Tooltip("Kamera arka plan — Canvas'ın arkasında görünür. Board'dan bir tık koyu olsun.")]
    public Color cameraBackground = new Color(0.022f, 0.030f, 0.090f, 1.00f);
}
