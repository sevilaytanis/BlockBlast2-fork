using UnityEditor;
using UnityEngine;

public static class BrandingSpriteImporter
{
    private static readonly string[] BrandingTexturePaths =
    {
        "Assets/Resources/Branding/culmin_studio_logo.png",
        "Assets/Resources/Branding/boomix_logo.png",
        "Assets/Resources/Branding/best_trophy_small.png",
        "Assets/Resources/Branding/best_trophy_large.png"
    };

    [MenuItem("Tools/Branding/Force Sprite Import Settings")]
    private static void ForceApply()
    {
        int updatedCount = 0;
        foreach (var path in BrandingTexturePaths)
        {
            if (ApplySpriteSettings(path)) updatedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"BrandingSpriteImporter: updated {updatedCount} texture(s) to Sprite (2D and UI).");
    }

    [InitializeOnLoadMethod]
    private static void ApplyOnEditorLoad()
    {
        // Delay call avoids importer access before asset database is ready.
        EditorApplication.delayCall += () =>
        {
            foreach (var path in BrandingTexturePaths)
            {
                ApplySpriteSettings(path);
            }
        };
    }

    private static bool ApplySpriteSettings(string assetPath)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (texture == null) return false;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return false;

        bool changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }

        return changed;
    }
}
