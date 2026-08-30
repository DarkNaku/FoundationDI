using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UINavigator의 캔버스 루트 프리팹을 만든다. 계층 조립은 런타임 폴백과 동일한
    /// UIRoot.CreateDefault()를 재사용하므로 코드 기본값과 프리팹이 어긋날 수 없다.
    /// </summary>
    public static class UIRootPrefabCreator
    {
        private const string DefaultFileName = "UIRoot.prefab";

        [MenuItem("Tools/FoundationDI/UI/Create UI Root Prefab", false, 60)]
        private static void CreateFromMenu()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create UI Root Prefab", DefaultFileName, "prefab",
                "UINavigator가 런타임에 인스턴스화할 캔버스 루트 프리팹을 저장할 위치를 선택하세요.");

            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateAt(path);

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>지정한 경로에 루트 프리팹을 저장하고 에셋의 UIRoot를 반환한다.</summary>
        public static UIRoot CreateAt(string assetPath)
        {
            var temp = UIRoot.CreateDefault();

            try
            {
                var saved = PrefabUtility.SaveAsPrefabAsset(temp.GO, assetPath);
                return saved != null ? saved.GetComponent<UIRoot>() : null;
            }
            finally
            {
                // 조립용 임시 오브젝트는 어떤 경우에도 씬에 남기지 않는다.
                Object.DestroyImmediate(temp.GO);
            }
        }
    }
}
