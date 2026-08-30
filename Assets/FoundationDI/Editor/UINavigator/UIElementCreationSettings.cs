using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UI 요소 생성 마법사의 프로젝트 기본값.
    /// EditorPrefs(머신 로컬)가 아니라 ProjectSettings에 저장한다 — 팀원 간 공유·커밋이 되어야
    /// 프로젝트 규약이 유지되기 때문이다.
    /// </summary>
    [FilePath("ProjectSettings/FoundationDIUIEditor.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UIElementCreationSettings : ScriptableSingleton<UIElementCreationSettings>
    {
        [SerializeField] private string _scriptRoot = "Assets/Scripts/UI";
        [SerializeField] private string _namespace = "";
        [SerializeField] private string _prefabRoot = "Assets/Resources/UI";

        public string ScriptRoot { get => _scriptRoot; set => _scriptRoot = value; }
        public string Namespace { get => _namespace; set => _namespace = value; }
        public string PrefabRoot { get => _prefabRoot; set => _prefabRoot = value; }

        public void Save() => Save(true);

        /// <summary>에셋 경로를 결합한다. 역슬래시를 정규화하고 슬래시 중복을 만들지 않는다.</summary>
        public static string CombineAssetPath(string root, string fileName)
        {
            var normalized = (root ?? string.Empty).Replace('\\', '/').TrimEnd('/');

            return normalized.Length == 0 ? fileName : $"{normalized}/{fileName}";
        }
    }

    internal static class UIElementCreationSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/FoundationDI/UI", SettingsScope.Project)
            {
                label = "UI",
                keywords = new[] { "FoundationDI", "UI", "UINavigator", "Page", "Popup", "Overlay" },
                guiHandler = _ =>
                {
                    var settings = UIElementCreationSettings.instance;

                    EditorGUI.BeginChangeCheck();

                    settings.ScriptRoot = EditorGUILayout.TextField("Script Root", settings.ScriptRoot);
                    settings.Namespace = EditorGUILayout.TextField("Namespace", settings.Namespace);
                    settings.PrefabRoot = EditorGUILayout.TextField("Prefab Root", settings.PrefabRoot);

                    EditorGUILayout.HelpBox(
                        "Prefab Root가 Resources 폴더 아래면 로드 키는 Resources 기준 상대 경로가 되고, " +
                        "그렇지 않으면 경로 전체가 Addressables 주소로 쓰입니다. " +
                        "후자의 경우 생성된 프리팹을 Addressables 그룹에 직접 추가해야 로드됩니다 " +
                        "(마법사는 주소만 계산하고 등록은 하지 않습니다).",
                        MessageType.Info);

                    if (EditorGUI.EndChangeCheck()) settings.Save();
                },
            };
        }
    }
}
