using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // 설정의 Use Play In-App Review와 의존성 XML을 맞춘다. 인스펙터 토글, 메뉴, 빌드 직전 세 곳에서 부른다.
    internal sealed class PlayReviewDependenciesSync : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Run();

        [MenuItem("Tools/FoundationDI/Review/Sync Play In-App Review Dependencies")]
        internal static void Run()
        {
            var flags = new List<bool>();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ReviewServiceSettings)))
            {
                var settings = AssetDatabase.LoadAssetAtPath<ReviewServiceSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null) flags.Add(settings.UsePlayInAppReview);
            }

            var root = Directory.GetParent(Application.dataPath).FullName;

            if (!PlayReviewDependenciesFile.Sync(PlayReviewDependenciesFile.ShouldExist(flags), root)) return;

            AssetDatabase.Refresh();
            Debug.Log("[ReviewService] Play In-App Review 의존성 XML을 갱신했다. EDM4U의 Android Resolver가 다음 해석에서 반영한다.");
        }
    }
}
