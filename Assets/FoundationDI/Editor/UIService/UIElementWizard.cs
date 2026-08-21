using System.Collections.Generic;
using System.IO;
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
            var viewPath = UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{_name}View.cs");
            var presenterPath = UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{_name}Presenter.cs");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("View", viewPath);
                EditorGUILayout.TextField("Presenter", presenterPath);
                EditorGUILayout.TextField("Prefab", prefabPath);
                EditorGUILayout.TextField("Key", UIElementNaming.ResolveResourceKey(prefabPath));
            }

            EditorGUILayout.Space();

            if (!valid && !string.IsNullOrEmpty(_name))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            // 프리팹뿐 아니라 File.WriteAllText로 직접 쓰는 두 스크립트도 막지 않으면, 프리팹만 지웠거나
            // PrefabRoot를 바꾼 뒤 같은 이름으로 다시 만들 때 이미 구현된 스크립트가 빈 스텁으로
            // 조용히 덮어써진다(AssetDatabase를 거치지 않아 휴지통에도 남지 않는다).
            var viewExists = !string.IsNullOrEmpty(_name) && File.Exists(viewPath);
            var presenterExists = !string.IsNullOrEmpty(_name) && File.Exists(presenterPath);
            var prefabExists = !string.IsNullOrEmpty(_name) && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var exists = viewExists || presenterExists || prefabExists;

            if (exists)
            {
                var collisions = new List<string>();

                if (viewExists) collisions.Add($"View 스크립트: {viewPath}");
                if (presenterExists) collisions.Add($"Presenter 스크립트: {presenterPath}");
                if (prefabExists) collisions.Add($"프리팹: {prefabPath}");

                EditorGUILayout.HelpBox($"이미 존재합니다 — {string.Join(", ", collisions)}", MessageType.Error);
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
