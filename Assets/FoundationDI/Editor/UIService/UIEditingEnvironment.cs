using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            // 경로를 고르게 하기 전에 먼저 막는다 — 그러지 않으면 사용자가 저장 위치를 정한 뒤에야
            // Build()에서 InvalidOperationException으로 터진다.
            if (IsBlockedByUnsavedScene())
            {
                EditorUtility.DisplayDialog("UI Editing Environment",
                    "저장된 적 없는 씬이 열려 있어 편집 환경 씬을 만들 수 없습니다.\n\n" +
                    "Unity는 이 상태에서 씬을 추가로 만들지 못합니다(\"Cannot create a new scene " +
                    "additively with an untitled scene unsaved\").\n\n" +
                    "열려 있는 씬을 저장하거나(File > Save As...) 이미 저장된 씬을 연 뒤 다시 실행하세요.\n" +
                    "PlayMode 테스트를 돌린 직후에도 이 상태가 됩니다.",
                    "확인");

                return;
            }

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

        /// <summary>
        /// 열린 씬 중 하나라도 저장된 적이 없으면(경로가 비면) true.
        /// Unity는 그 상태에서 <see cref="EditorSceneManager.NewScene"/>의 Additive 생성을 거부한다.
        /// 사용자의 미저장 씬을 대신 저장하거나 버릴 수는 없으므로 감지해서 안내만 한다.
        /// 판정 조건은 "수정됨"이 아니라 "한 번도 저장된 적 없음"이다 — 깨끗한 untitled 씬도 막힌다.
        /// </summary>
        internal static bool IsBlockedByUnsavedScene(IReadOnlyList<string> loadedScenePaths)
        {
            for (int i = 0; i < loadedScenePaths.Count; i++)
            {
                if (string.IsNullOrEmpty(loadedScenePaths[i])) return true;
            }

            return false;
        }

        private static bool IsBlockedByUnsavedScene()
        {
            var paths = new string[SceneManager.sceneCount];

            for (int i = 0; i < paths.Length; i++) paths[i] = SceneManager.GetSceneAt(i).path;

            return IsBlockedByUnsavedScene(paths);
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
