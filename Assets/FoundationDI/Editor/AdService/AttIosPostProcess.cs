#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace DarkNaku.FoundationDI.Editor
{
    // iOS 빌드의 Info.plist에 ATT 동의 문구(NSUserTrackingUsageDescription)를 넣는다.
    //
    // 유니티 플레이어 설정에는 이 키를 넣을 자리가 없다. 그리고 이 문구가 없으면 iOS는
    // 다이얼로그를 아예 띄우지 않고 — 즉 ATTrackingStatusBinding을 아무리 제대로 불러도
    // 동의를 영영 못 받고 — 앱 심사에서도 리젝된다. 빌드 후처리로 못 박아 두는 편이
    // "빌드할 때마다 손으로 Xcode에서 추가"보다 안전하다.
    public static class AttIosPostProcess
    {
        internal const string UsageDescriptionKey = "NSUserTrackingUsageDescription";

        // 이 문구가 다이얼로그 본문에 그대로 나온다.
        internal const string UsageDescription =
            "We use your activity data to provide personalized ads and measure ad performance.";

        // 이미 값이 있으면 건드리지 않는다. 프로젝트가 자기 문구(다국어 포함)를 다른
        // 후처리로 넣었을 수 있고, 그걸 덮으면 조용히 되돌아가는 것처럼 보인다.
        internal static string Resolve(string existing)
        {
            return string.IsNullOrWhiteSpace(existing) ? UsageDescription : existing;
        }

#if UNITY_IOS && FOUNDATIONDI_IOSSUPPORT
        // 999는 FDI_HapticiOSPostProcess가 쓰는 값이다. 같은 plist를 만지는 다른 후처리와
        // 섞이지 않도록 그 뒤에 선다.
        [PostProcessBuild(1000)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            WriteUsageDescription(path);
            WeakLinkFramework(path);
        }

        private static void WriteUsageDescription(string path)
        {
            var plistPath = Path.Combine(path, "Info.plist");

            if (!File.Exists(plistPath)) return;

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            var existing = plist.root.values.TryGetValue(UsageDescriptionKey, out var element)
                ? (element as PlistElementString)?.value
                : null;

            plist.root.SetString(UsageDescriptionKey, Resolve(existing));
            plist.WriteToFile(plistPath);
        }

        // 패키지의 네이티브 플러그인이 AppTrackingTransparency를 임포트한다. iOS 14+에만
        // 있는 프레임워크라 weak-link여야 그 아래 버전에서 실행 시 죽지 않는다.
        private static void WeakLinkFramework(string path)
        {
            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();

            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(),
                                          "AppTrackingTransparency.framework", true);
            project.WriteToFile(projectPath);
        }
#endif
    }
}
