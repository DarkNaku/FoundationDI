using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // 설정의 Use Play App Update와 의존성 XML을 맞춘다. 인스펙터 토글, 메뉴, 빌드 직전 세 곳에서 부른다.
    internal sealed class PlayAppUpdateDependenciesSync : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Run();

        [MenuItem("Tools/FoundationDI/App Update/Sync Play In-App Updates Dependencies")]
        internal static void Run()
        {
            var flags = new List<bool>();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(AppUpdateServiceSettings)))
            {
                var settings = AssetDatabase.LoadAssetAtPath<AppUpdateServiceSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null) flags.Add(settings.UsePlayAppUpdate);
            }

            var root = Directory.GetParent(Application.dataPath).FullName;

            if (!PlayAppUpdateDependenciesFile.Sync(PlayAppUpdateDependenciesFile.ShouldExist(flags), root)) return;

            AssetDatabase.Refresh();
            Debug.Log("[AppUpdateService] Play In-App Updates 의존성 XML을 갱신했다. EDM4U의 Android Resolver가 다음 해석에서 반영한다.");
        }
    }
}
