#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

// Editörde otomatik olarak Resources/BlockPalette.asset oluşturur.
// Play'e basıldığında veya proje yüklendiğinde bir kez çalışır.
// Asset zaten varsa dokunmaz.
[InitializeOnLoad]
public static class BlockPaletteBootstrap
{
    const string ResourcePath = "Assets/Resources/BlockPalette.asset";

    static BlockPaletteBootstrap()
    {
        EditorApplication.delayCall += EnsurePaletteAsset;
    }

    [MenuItem("BlockBlast/Create Block Palette Asset")]
    public static void EnsurePaletteAsset()
    {
        if (File.Exists(Path.Combine(Application.dataPath, "../" + ResourcePath))
            || AssetDatabase.LoadAssetAtPath<BlockPaletteData>(ResourcePath) != null)
            return;

        // Resources klasörünü oluştur (yoksa)
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var palette = ScriptableObject.CreateInstance<BlockPaletteData>();
        AssetDatabase.CreateAsset(palette, ResourcePath);
        AssetDatabase.SaveAssets();

        Debug.Log("[BlockBlast] BlockPalette.asset oluşturuldu: " + ResourcePath +
                  "\nAsset panelinden düzenleyebilirsin: Assets/Resources/BlockPalette");
    }
}
#endif
