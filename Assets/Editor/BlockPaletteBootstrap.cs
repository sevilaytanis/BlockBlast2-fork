#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Editörde otomatik olarak Resources/BlockPalette.asset ve Resources/GridSettings.asset oluşturur.
// Play'e basıldığında veya proje yüklendiğinde bir kez çalışır. Asset zaten varsa dokunmaz.
//
// MEVCUT asset'leri güncelle:
//   Unity menüsü → BlockBlast → Reset Visual Assets to Defaults
[InitializeOnLoad]
public static class BlockPaletteBootstrap
{
    const string PalettePath     = "Assets/Resources/BlockPalette.asset";
    const string GridSettingPath = "Assets/Resources/GridSettings.asset";

    static BlockPaletteBootstrap()
    {
        EditorApplication.delayCall += EnsureAssets;
    }

    // ── Yoksa oluştur ─────────────────────────────────────────────────────────

    [MenuItem("BlockBlast/Create Visual Assets")]
    public static void EnsureAssets()
    {
        bool created = false;

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        if (AssetDatabase.LoadAssetAtPath<BlockPaletteData>(PalettePath) == null)
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BlockPaletteData>(), PalettePath);
            created = true;
            Debug.Log("[BlockBlast] BlockPalette.asset oluşturuldu: " + PalettePath);
        }

        if (AssetDatabase.LoadAssetAtPath<GridSettings>(GridSettingPath) == null)
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GridSettings>(), GridSettingPath);
            created = true;
            Debug.Log("[BlockBlast] GridSettings.asset oluşturuldu: " + GridSettingPath);
        }

        if (created)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[BlockBlast] Assets/Resources klasöründen renk ayarlarını düzenleyebilirsin.");
        }
    }

    // ── Mevcut asset'leri en son önerilen değerlere sıfırla ───────────────────
    // Grid çok baskın görünüyorsa, bloklar soluk kalıyorsa: bu menüyü çalıştır.

    [MenuItem("BlockBlast/Reset Visual Assets to Defaults")]
    public static void ResetToDefaults()
    {
        EnsureAssets();

        // ── GridSettings ─────────────────────────────────────────────────────
        var gs = AssetDatabase.LoadAssetAtPath<GridSettings>(GridSettingPath);
        if (gs != null)
        {
            // Board — siyaha yakın derin lacivert (#0A0E25)
            gs.boardSurface     = new Color(0.039f, 0.055f, 0.145f, 1.00f);
            gs.boardShadow      = new Color(0.008f, 0.012f, 0.035f, 0.80f);
            gs.boardInset       = new Color(0f,     0f,     0f,     0.28f);

            // Hücre — neredeyse görünmez rehber çizgisi + board'dan koyu iç
            gs.cellEdge         = new Color(0.20f,  0.23f,  0.45f,  0.07f); // alpha 0.07 hayalet
            gs.cellCenter       = new Color(0.018f, 0.022f, 0.060f, 0.92f); // #050616 recessed
            gs.cellHighlightAlpha = 0.00f;

            // Tray — board ile aynı
            gs.trayPanel        = new Color(0.039f, 0.055f, 0.145f, 1.00f);
            gs.trayInset        = new Color(0f,     0f,     0f,     0.28f);
            gs.trayShadow       = new Color(0f,     0f,     0f,     0.60f);

            // Kamera
            gs.cameraBackground = new Color(0.022f, 0.030f, 0.090f, 1.00f);

            EditorUtility.SetDirty(gs);
            Debug.Log("[BlockBlast] GridSettings güncellendi.");
        }

        // ── BlockPalette ─────────────────────────────────────────────────────
        var palette = AssetDatabase.LoadAssetAtPath<BlockPaletteData>(PalettePath);
        if (palette != null)
        {
            // Emission: koyu zemin üzerinde blokların parlaması için artırıldı
            palette.emissionIntensity = 0.55f;

            // Neon renkler — yüksek doygunluklu
            palette.blockColors = new Color[]
            {
                new Color(0.60f, 0.10f, 1.00f, 1f), // Neon Violet  — #9919FF
                new Color(0.10f, 0.88f, 0.20f, 1f), // Bright Green — #1AE133
                new Color(0.12f, 0.44f, 1.00f, 1f), // Deep Blue    — #1E70FF
                new Color(1.00f, 0.48f, 0.04f, 1f), // Bright Orange— #FF7A0A
                new Color(1.00f, 0.12f, 0.18f, 1f), // Fire Red     — #FF1F2E
                new Color(1.00f, 0.22f, 0.76f, 1f), // Hot Pink     — #FF38C2
                new Color(0.72f, 1.00f, 0.04f, 1f), // Neon Lime    — #B8FF0A
            };

            EditorUtility.SetDirty(palette);
            Debug.Log("[BlockBlast] BlockPalette güncellendi (emission: 0.55).");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[BlockBlast] Tüm görsel asset'ler güncellendi. Play'e basarak sonucu gör.");
    }
}
#endif
