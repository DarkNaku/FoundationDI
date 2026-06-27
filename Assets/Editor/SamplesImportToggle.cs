using System.IO;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.EditorTools
{
    /// <summary>
    /// UPM 샘플 폴더의 '~' 접미사를 토글한다.
    /// - Samples~ : Unity가 임포트하지 않음(배포/저장소 기본 상태).
    /// - Samples  : Unity가 임포트함 → 씬을 열어 바로 Play 테스트 가능.
    /// 폴더 내부의 .meta(프리팹/씬/스크립트 GUID)는 그대로 유지되므로
    /// 토글해도 샘플 내부 참조가 끊기지 않는다. 폴더 자체의 .meta만 관리한다.
    /// </summary>
    internal static class SamplesImportToggle
    {
        private const string PackageRoot = "Assets/FoundationDI";
        private const string ImportedName = "Samples";
        private const string ExcludedName = "Samples~";
        private const string MenuPath = "Tools/FoundationDI/샘플 임포트(테스트용)";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string ImportedAbs => Path.Combine(ProjectRoot, PackageRoot, ImportedName);
        private static string ExcludedAbs => Path.Combine(ProjectRoot, PackageRoot, ExcludedName);

        [MenuItem(MenuPath, false, 0)]
        private static void Toggle()
        {
            if (Directory.Exists(ImportedAbs))
            {
                // 켜짐 → 끄기 : Samples → Samples~
                Directory.Move(ImportedAbs, ExcludedAbs);

                // 폴더 자체의 .meta는 임포트 시 새로 생성된 것이므로 제거.
                var folderMeta = ImportedAbs + ".meta";
                if (File.Exists(folderMeta)) File.Delete(folderMeta);

                AssetDatabase.Refresh();
                Debug.Log("[FoundationDI] 샘플 임포트 OFF — 'Samples~' (Unity 무시, 저장소 기본 상태)");
            }
            else if (Directory.Exists(ExcludedAbs))
            {
                // 꺼짐 → 켜기 : Samples~ → Samples
                Directory.Move(ExcludedAbs, ImportedAbs);
                AssetDatabase.Refresh();
                Debug.Log("[FoundationDI] 샘플 임포트 ON — 'Samples' (씬을 열어 바로 Play 테스트 가능). " +
                          "커밋 전에는 다시 OFF로 되돌리세요.");
            }
            else
            {
                Debug.LogWarning($"[FoundationDI] 샘플 폴더를 찾을 수 없습니다: {PackageRoot}/{ImportedName} 또는 /{ExcludedName}");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Directory.Exists(ImportedAbs));
            return Directory.Exists(ImportedAbs) || Directory.Exists(ExcludedAbs);
        }
    }
}
