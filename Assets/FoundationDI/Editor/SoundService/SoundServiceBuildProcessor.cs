using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>빌드 직전에 SoundService 데이터 참조가 모두 연결되어 있는지 확인한다.</summary>
    public class SoundServiceBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SoundServiceAssetLocator.FindSettings();

            if (settings == null)
            {
                Debug.LogWarning("[SoundService] SoundServiceSettings 에셋을 찾지 못했습니다. " +
                                 "SoundService를 쓰지 않는 프로젝트라면 무시해도 됩니다.");
                return;
            }

            if (settings.SoundDataCollection == null || settings.MusicDataCollection == null ||
                settings.OutputDataCollection == null)
            {
                Debug.LogError("[SoundService] SoundServiceSettings의 데이터 컬렉션 참조가 비어 있습니다. " +
                               "Tools > DarkNaku > FoundationDI > Sound > Settings에서 확인하세요.");
            }
        }
    }
}
