using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>이름과 모드만 받아 UI 요소를 생성하는 마법사.</summary>
    public sealed class UIElementWizard : EditorWindow
    {
        private string _name = "";
        private UIElementMode _mode = UIElementMode.Page;

        [MenuItem("Tools/FoundationDI/UI/Create UI Element...", false, 70)]
        private static void Open()
        {
            var window = GetWindow<UIElementWizard>(true, "Create UI Element", true);

            window.minSize = new Vector2(460f, 220f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            var settings = UIElementCreationSettings.instance;

            EditorGUILayout.LabelField("새 UI 요소", EditorStyles.boldLabel);

            _name = EditorGUILayout.TextField("Name", _name);
            _mode = (UIElementMode)EditorGUILayout.EnumPopup("Mode", _mode);

            EditorGUILayout.Space();

            var valid = UIElementNaming.TryValidate(_name, out var error);
            var prefabPath = UIElementCreationSettings.CombineAssetPath(settings.PrefabRoot, $"{_name}.prefab");
            var scriptRoot = settings.ScriptRoot;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("View", UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{_name}View.cs"));
                EditorGUILayout.TextField("Presenter", UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{_name}Presenter.cs"));
                EditorGUILayout.TextField("Prefab", prefabPath);
                EditorGUILayout.TextField("Key", UIElementNaming.ResolveResourceKey(prefabPath));
            }

            EditorGUILayout.Space();

            if (!valid && !string.IsNullOrEmpty(_name))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            var exists = !string.IsNullOrEmpty(_name)
                         && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;

            if (exists)
            {
                EditorGUILayout.HelpBox($"이미 존재합니다: {prefabPath}", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(!valid || exists))
            {
                if (GUILayout.Button("Create", GUILayout.Height(28f)))
                {
                    UIElementCreator.Begin(new UIElementCreationRequest
                    {
                        Name = _name,
                        Mode = _mode,
                        Namespace = settings.Namespace,
                        PrefabPath = prefabPath,
                    });

                    Close();
                }
            }

            EditorGUILayout.HelpBox(
                "경로와 네임스페이스 기본값은 Project Settings > FoundationDI > UI 에서 바꿉니다.",
                MessageType.Info);
        }
    }
}
