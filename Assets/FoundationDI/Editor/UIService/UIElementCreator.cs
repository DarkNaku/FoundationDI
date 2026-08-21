using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UI 요소(View 스크립트 + Presenter 스크립트 + 프리팹)를 생성한다.
    ///
    /// 스크립트를 만든 직후에는 그 타입이 아직 컴파일되지 않아 AddComponent가 불가능하다.
    /// 그래서 도메인 리로드를 경계로 2단계로 나눈다:
    ///   Begin()  — 스크립트를 쓰고 요청을 SessionState에 남긴 뒤 Refresh
    ///   Resume() — 리로드 후 [DidReloadScripts]에서 프리팹을 조립하고 프리팹 모드로 진입
    /// </summary>
    public static class UIElementCreator
    {
        private const string PendingKey = "DarkNaku.FoundationDI.UIElementCreator.Pending";

        public static void Begin(UIElementCreationRequest request)
        {
            var scriptRoot = UIElementCreationSettings.instance.ScriptRoot;
            var viewPath = UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{request.Name}View.cs");
            var presenterPath = UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{request.Name}Presenter.cs");
            var resourceKey = UIElementNaming.ResolveResourceKey(request.PrefabPath);

            // 폴더 생성은 스크립트를 쓰기 전에 끝낸다: 이 시점에는 새 .cs 에셋이 아직 없으므로
            // EnsureFolder 내부의 Refresh()가 컴파일·도메인 리로드를 유발하지 않는다.
            // SessionState.SetString은 반드시 "컴파일을 유발할 수 있는" 마지막 Refresh보다 먼저 실행되어야 한다 —
            // 그렇지 않으면 대기 작업이 저장되기 전에 리로드가 일어나 파이프라인이 조용히 죽는다.
            EnsureFolder(scriptRoot);
            EnsureFolder(Path.GetDirectoryName(request.PrefabPath)?.Replace('\\', '/'));

            File.WriteAllText(viewPath, UIElementTemplates.View(request.Namespace, request.Name));
            File.WriteAllText(presenterPath,
                UIElementTemplates.Presenter(request.Namespace, request.Name, request.Mode, resourceKey));

            // 리로드 후에도 살아남아야 한다.
            SessionState.SetString(PendingKey, request.ToJson());

            AssetDatabase.Refresh();
        }

        [DidReloadScripts]
        private static void Resume()
        {
            var json = SessionState.GetString(PendingKey, string.Empty);

            if (string.IsNullOrEmpty(json)) return;

            // 성공하든 실패하든 대기 작업은 여기서 지운다. 좀비 상태로 남기지 않는다.
            SessionState.EraseString(PendingKey);

            var request = UIElementCreationRequest.FromJson(json);

            if (request == null)
            {
                Debug.LogError("[FoundationDI] UI 요소 생성 요청을 복원하지 못했습니다. 마법사를 다시 실행하세요.");
                return;
            }

            var viewType = FindViewType(request);

            if (viewType == null)
            {
                Debug.LogError(
                    $"[FoundationDI] '{request.Name}View' 타입을 찾지 못해 프리팹 생성을 중단했습니다. " +
                    "컴파일 에러가 있는지 확인한 뒤 마법사를 다시 실행하세요.");
                return;
            }

            var go = UIElementPrefabBuilder.Build(viewType, request.Mode);
            GameObject prefab;

            try
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(go, request.PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            if (prefab == null)
            {
                Debug.LogError($"[FoundationDI] 프리팹 저장에 실패했습니다: {request.PrefabPath}");
                return;
            }

            var prefabPath = request.PrefabPath;

            // [DidReloadScripts] 시점에는 에디터 창/도킹 시스템이 아직 완전히 자리잡지 않아
            // Selection 변경이나 AssetDatabase.OpenAsset을 그 자리에서 호출하면 프리팹 모드가
            // 조용히 열리지 않을 수 있다. 다음 에디터 업데이트로 미뤄야 안정적으로 진입한다.
            // 이 시점에 붙잡은 GameObject 참조도 그 사이의 재임포트로 무효화될 수 있으므로,
            // 경로만 캡처해 실행 시점에 AssetDatabase에서 새로 불러온다.
            EditorApplication.delayCall += () =>
            {
                var freshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (freshPrefab == null)
                {
                    Debug.LogError($"[FoundationDI] 프리팹을 다시 불러오지 못했습니다: {prefabPath}");
                    return;
                }

                Selection.activeObject = freshPrefab;

                // 격리 프리팹 모드로 진입 → UI 편집 환경이 적용된 상태로 바로 작업 가능.
                AssetDatabase.OpenAsset(freshPrefab);

                Debug.Log($"[FoundationDI] '{request.Name}' {request.Mode} 생성 완료: {prefabPath}");
            };
        }

        private static Type FindViewType(UIElementCreationRequest request)
        {
            var typeName = string.IsNullOrWhiteSpace(request.Namespace)
                ? $"{request.Name}View"
                : $"{request.Namespace}.{request.Name}View";

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName, false);

                if (type != null) return type;
            }

            return null;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

            Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
        }
    }
}
