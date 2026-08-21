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

            Debug.Log($"[FoundationDI] UI 프리팹 편집 환경을 '{path}'로 지정했습니다(루트 프리팹: '{rootPrefab.name}'). " +
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

        // UIServiceSettings에 지정된 루트 프리팹을 최우선으로 쓴다 — 이 기능 자체가 "무엇을 편집 중인지"의
        // 드리프트를 없애려는 목적이므로, 선택 항목이 다른 프리팹이라도 조용히 갈아타면 그 목적에 반한다.
        // Settings에 아직 아무것도 연결되지 않았을 때만 현재 선택으로 폴백한다.
        private static UIRoot PromptForRootPrefab()
        {
            var fromSettings = FindSettingsRootPrefab();

            if (fromSettings != null) return fromSettings;

            var fromSelection = ResolveSelectedRootPrefabAsset();

            if (fromSelection != null) return fromSelection;

            EditorUtility.DisplayDialog("UI Editing Environment",
                "루트 프리팹을 찾지 못했습니다.\n\n" +
                "Tools/FoundationDI/UI/Create UI Root Prefab 으로 먼저 프리팹을 만들고 " +
                "UIServiceSettings에 연결하거나, 프로젝트 창에서 루트 프리팹을 선택한 뒤 다시 실행하세요.",
                "확인");

            return null;
        }

        internal static UIRoot FindSettingsRootPrefab()
        {
            var guids = AssetDatabase.FindAssets("t:UIServiceSettings");

            foreach (var guid in guids)
            {
                var settings = AssetDatabase.LoadAssetAtPath<UIServiceSettings>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (settings != null && settings.RootPrefab != null) return settings.RootPrefab;
            }

            return null;
        }

        // 선택 항목이 프리팹 에셋 자체가 아니라 씬 안의 인스턴스(디자인 스펙이 권장하는 "드래그해서 쓰고
        // 지운다" 플로우)여도 원본 에셋으로 되짚는다. 이전에는 씬 인스턴스를 그대로
        // PrefabUtility.InstantiatePrefab에 넘겨 null을 돌려받고 NRE로 죽었다.
        internal static UIRoot ResolveSelectedRootPrefabAsset()
        {
            var selected = Selection.activeGameObject;

            if (selected == null) return null;

            var root = selected.GetComponent<UIRoot>();

            if (root == null) return null;

            if (PrefabUtility.IsPartOfPrefabAsset(root.gameObject)) return root;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(root.gameObject);

            return source != null ? source.GetComponent<UIRoot>() : null;
        }
    }
}
