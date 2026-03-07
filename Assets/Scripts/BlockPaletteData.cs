using UnityEngine;

// Vibrant Crystal blok paleti — Asset panelinden düzenlenebilir.
// Oluşturmak için: Assets menüsü → Create → BlockBlast → Block Palette
// Runtime'da Resources/BlockPalette.asset olarak yüklenir.
[CreateAssetMenu(fileName = "BlockPalette", menuName = "BlockBlast/Block Palette", order = 1)]
public class BlockPaletteData : ScriptableObject
{
    [Header("Block Colors — vibrant neon")]
    [Tooltip("Tray ve grid bloklarında kullanılan neon renk paleti. Buradan istediğin rengi değiştirebilirsin.")]
    public Color[] blockColors = new Color[]
    {
        new Color(0.60f, 0.10f, 1.00f, 1f), // Neon Violet  — #9919FF
        new Color(0.10f, 0.88f, 0.20f, 1f), // Bright Green — #1AE133
        new Color(0.12f, 0.44f, 1.00f, 1f), // Deep Blue    — #1E70FF
        new Color(1.00f, 0.48f, 0.04f, 1f), // Bright Orange— #FF7A0A
        new Color(1.00f, 0.12f, 0.18f, 1f), // Fire Red     — #FF1F2E
        new Color(1.00f, 0.22f, 0.76f, 1f), // Hot Pink     — #FF38C2
        new Color(0.72f, 1.00f, 0.04f, 1f), // Neon Lime    — #B8FF0A
    };

    [Header("Empty Cell Visuals")]
    [Tooltip("Izgara çizgisi (rim) rengi. Alpha değeri opaklığı belirler — 0.25 önerilir.")]
    public Color cellRimColor  = new Color(0.84f, 0.90f, 0.98f, 0.25f);

    [Tooltip("Boş hücre iç dolgu rengi.")]
    public Color cellFillColor = new Color(0.006f, 0.008f, 0.015f, 0.34f);

    [Header("Block Emission / Glow")]
    [Range(0f, 1.0f)]
    [Tooltip(
        "Inner glow (emission) şiddeti — blokların karanlık zemin üzerinde parlamasını sağlar.\n" +
        "Siyaha yakın board üzerinde 0.50-0.60 önerilir.")]
    public float emissionIntensity = 0.55f;
}
