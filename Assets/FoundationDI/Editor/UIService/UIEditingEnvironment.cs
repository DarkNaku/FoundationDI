using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UI 프리팹을 "런타임과 같은 캔버스 안에서" 편집할 수 있도록,
    /// 루트 프리팹 인스턴스 하나만 든 씬을 만들어 EditorSettings.prefabUIEnvironment에 지정한다.
    /// 이 설정은 프리팹을 "격리(isolation) 모드"로 열 때만 적용된다
    /// (= 프로젝트 창에서 더블클릭했을 때).
    /// 프로젝트 설정을 바꾸므로 자동 실행 없이 명시적 메뉴 실행으로만 동작한다.
    /// </summary>
    public static class UIEditingEnvironment
    {
        private const string DefaultFileName = "UIEditingEnvironment.unity";

        [MenuItem("Tools/FoundationDI/UI/Setup Prefab Editing Environment", false, 61)]
        private static void SetupFromMenu()
        {
            var rootPrefab = PromptForRootPrefab();

            if (rootPrefab == null) return;

            var path = EditorUtility.SaveFilePanelInProject(
                "Create UI Editing Environment Scene", DefaultFileName, "unity",
                "UI 프리팹 편집 환경으로 쓸 씬을 저장할 위치를 선택하세요.");

            if (string.IsNullOrEmpty(path)) return;

            var scene = Build(path, rootPrefab);

            if (scene == null) return;

            Assign(scene);

            Debug.Log($"[FoundationDI] UI 프리팹 편집 환경을 '{path}'로 지정했습니다. " +
                      "이제 UI 프리팹을 더블클릭하면 실제 캔버스 안에서 열립니다.");
        }

        [MenuItem("Tools/FoundationDI/UI/Clear Prefab Editing Environment", false, 62)]
        private static void ClearFromMenu()
        {
            Clear();
            Debug.Log("[FoundationDI] UI 프리팹 편집 환경 지정을 해제했습니다.");
        }

        public static void Assign(SceneAsset scene) => EditorSettings.prefabUIEnvironment = scene;

        public static void Clear() => EditorSettings.prefabUIEnvironment = null;

        /// <summary>루트 프리팹 인스턴스 하나만 든 씬을 저장하고 SceneAsset을 반환한다.</summary>
        public static SceneAsset Build(string scenePath, UIRoot rootPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(rootPrefab.GO, scene);

                instance.name = rootPrefab.name;

                if (!EditorSceneManager.SaveScene(scene, scenePath)) return null;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        }

        // Settings 에셋이 하나뿐이면 그것의 루트 프리팹을, 아니면 선택된 프리팹을 쓴다.
        private static UIRoot PromptForRootPrefab()
        {
            var selected = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<UIRoot>()
                : null;

            if (selected != null) return selected;

            var guids = AssetDatabase.FindAssets("t:UIServiceSettings");

            foreach (var guid in guids)
            {
                var settings = AssetDatabase.LoadAssetAtPath<UIServiceSettings>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (settings != null && settings.RootPrefab != null) return settings.RootPrefab;
            }

            EditorUtility.DisplayDialog("UI Editing Environment",
                "루트 프리팹을 찾지 못했습니다.\n\n" +
                "Tools/FoundationDI/UI/Create UI Root Prefab 으로 먼저 프리팹을 만들고 " +
                "UIServiceSettings에 연결하거나, 프로젝트 창에서 루트 프리팹을 선택한 뒤 다시 실행하세요.",
                "확인");

            return null;
        }
    }
}
