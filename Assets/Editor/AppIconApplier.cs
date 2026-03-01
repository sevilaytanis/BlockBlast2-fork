using UnityEditor;
using UnityEngine;

public static class AppIconApplier
{
    private const string IconAssetPath = "Assets/AppIcons/app_icon_1024.png";

    [MenuItem("Tools/App Icon/Apply Default Icon")]
    private static void ApplyDefaultIcon()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
        if (icon == null)
        {
            EditorUtility.DisplayDialog(
                "App Icon Not Found",
                $"Icon file not found at:\n{IconAssetPath}\n\nPlace your 1024x1024 PNG there and run again.",
                "OK");
            return;
        }

        if (icon.width != icon.height)
        {
            EditorUtility.DisplayDialog(
                "Invalid Icon",
                $"Icon must be square. Current size: {icon.width}x{icon.height}",
                "OK");
            return;
        }

        ApplyForTargetGroup(BuildTargetGroup.iOS, icon);
        ApplyForTargetGroup(BuildTargetGroup.Android, icon);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("App Icon", "Icon applied for iOS and Android.", "OK");
    }

    private static void ApplyForTargetGroup(BuildTargetGroup group, Texture2D icon)
    {
        var sizes = PlayerSettings.GetIconSizesForTargetGroup(group);
        var icons = new Texture2D[sizes.Length];

        for (var i = 0; i < icons.Length; i++)
        {
            icons[i] = icon;
        }

        PlayerSettings.SetIconsForTargetGroup(group, icons);
    }
}
