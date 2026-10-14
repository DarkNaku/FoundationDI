using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // 설정의 Use UMP와 의존성 XML을 맞춘다. 인스펙터 토글, 메뉴, 빌드 직전 세 곳에서 부른다.
    internal sealed class UmpDependenciesSync : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Run();

        [MenuItem("Tools/FoundationDI/Consent/Sync UMP Dependencies")]
        internal static void Run()
        {
            var useUmp = ConsentServiceSettingsLocator.FindAll().Select(s => s.UseUmp);
            var root = Directory.GetParent(Application.dataPath).FullName;

            if (!UmpDependenciesFile.Sync(UmpDependenciesFile.ShouldExist(useUmp), root)) return;

            AssetDatabase.Refresh();
            Debug.Log("[ConsentService] UMP 의존성 XML을 갱신했다. EDM4U의 Android/iOS Resolver가 다음 해석에서 반영한다.");
        }

        // EDM4U(External Dependency Manager)가 없으면 XML을 만들어도 아무도 읽지 않는다.
        internal static bool IsEdm4uInstalled() =>
            System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Google.VersionHandler"));
    }
}
