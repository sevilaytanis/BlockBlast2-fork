#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

// Ensures iOS build uses the branded LaunchScreen.storyboard.
public static class iOSLaunchScreenPostprocess
{
    [PostProcessBuild(999)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string sourceStoryboard = Path.Combine(Application.dataPath, "Plugins/iOS/LaunchScreen.storyboard");
        string destinationStoryboard = Path.Combine(pathToBuiltProject, "LaunchScreen.storyboard");
        bool hasStoryboard = File.Exists(sourceStoryboard);
        if (hasStoryboard)
            File.Copy(sourceStoryboard, destinationStoryboard, true);
        else
            Debug.LogWarning("[iOSLaunchScreenPostprocess] LaunchScreen.storyboard not found in Assets/Plugins/iOS.");

        string sourcePrivacyManifest = Path.Combine(Application.dataPath, "Plugins/iOS/PrivacyInfo.xcprivacy");
        string destinationPrivacyManifest = Path.Combine(pathToBuiltProject, "PrivacyInfo.xcprivacy");
        if (File.Exists(sourcePrivacyManifest))
            File.Copy(sourcePrivacyManifest, destinationPrivacyManifest, true);
        else
            Debug.LogWarning("[iOSLaunchScreenPostprocess] PrivacyInfo.xcprivacy not found in Assets/Plugins/iOS.");

        string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var pbx = new PBXProject();
        pbx.ReadFromFile(pbxPath);

        string mainTarget = pbx.GetUnityMainTargetGuid();
        if (hasStoryboard)
        {
            string fileGuid = pbx.AddFile(destinationStoryboard, "LaunchScreen.storyboard", PBXSourceTree.Source);
            pbx.AddFileToBuild(mainTarget, fileGuid);
        }

        if (File.Exists(destinationPrivacyManifest))
        {
            string privacyGuid = pbx.AddFile(destinationPrivacyManifest, "PrivacyInfo.xcprivacy", PBXSourceTree.Source);
            pbx.AddFileToBuild(mainTarget, privacyGuid);
        }
        pbx.WriteToFile(pbxPath);

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        if (hasStoryboard)
            plist.root.SetString("UILaunchStoryboardName", "LaunchScreen");
        // Force iPhone-only build (disable iPad support in the exported Xcode project).
        var deviceFamily = plist.root.CreateArray("UIDeviceFamily");
        deviceFamily.AddInteger(1); // 1 = iPhone/iPod touch
        plist.WriteToFile(plistPath);

        Debug.Log("[iOSLaunchScreenPostprocess] LaunchScreen.storyboard copied and wired.");
    }
}
#endif
