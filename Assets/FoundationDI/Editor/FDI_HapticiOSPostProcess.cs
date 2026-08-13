#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace DarkNaku.FoundationDI.Editor
{
    public static class FDI_HapticiOSPostProcess
    {
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string fw = project.GetUnityFrameworkTargetGuid();
            // CoreHaptics는 iOS13+라 weak-link
            project.AddFrameworkToProject(fw, "CoreHaptics.framework", true);
            project.AddFrameworkToProject(fw, "UIKit.framework", false);

            project.WriteToFile(projectPath);
        }
    }
}
#endif
