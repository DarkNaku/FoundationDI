using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// UI 캔버스 루트. 캔버스 설정과 레이어 구성은 이 컴포넌트가 붙은 "프리팹"이 결정한다.
    /// 인스턴스화 시 코드가 어떤 값도 덮어쓰지 않는다 — 코드가 이 값들을 다루는 유일한 곳은
    /// CreateDefault()이며, 그것은 "기본 프리팹을 조립하는 템플릿"이다.
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
    public sealed class UIRoot : MonoBehaviour
    {
        public static readonly Vector2 DefaultReferenceResolution = new(1920f, 1080f);

        [SerializeField] private RectTransform _pageLayer;
        [SerializeField] private RectTransform _belowOverlayLayer;
        [SerializeField] private RectTransform _popupLayer;
        [SerializeField] private RectTransform _aboveOverlayLayer;

        public GameObject GO => gameObject;
        public Transform PageLayer => _pageLayer;
        public Transform BelowOverlayLayer => _belowOverlayLayer;
        public Transform PopupLayer => _popupLayer;
        public Transform AboveOverlayLayer => _aboveOverlayLayer;

        /// <summary>
        /// 비어 있는 레이어 필드 이름 목록을 반환한다(없으면 null).
        /// UIService.CreateRoot()의 런타임 검증과 아래 OnValidate의 저작 시점 검증이 이 로직을 공유한다.
        /// </summary>
        internal List<string> GetMissingLayerNames()
        {
            List<string> missing = null;

            if (_pageLayer == null) (missing ??= new()).Add(nameof(PageLayer));
            if (_belowOverlayLayer == null) (missing ??= new()).Add(nameof(BelowOverlayLayer));
            if (_popupLayer == null) (missing ??= new()).Add(nameof(PopupLayer));
            if (_aboveOverlayLayer == null) (missing ??= new()).Add(nameof(AboveOverlayLayer));

            return missing;
        }

#if UNITY_EDITOR
        // 인스펙터 편집·프리팹 로드 시 레이어 미연결을 저작 시점에 잡는다.
        // CreateDefault()/프리팹 조립 도중에는 필드가 아직 다 채워지지 않은 채로 호출될 수 있어
        // delayCall로 한 프레임 미룬 뒤 최종 상태를 검사한다(CameraFitter.OnValidate와 동일 패턴).
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += ValidateLayersFromEditor;
        }

        private void ValidateLayersFromEditor()
        {
            if (this == null) return; // delayCall 사이에 파괴되었을 수 있음

            var missing = GetMissingLayerNames();

            if (missing != null)
            {
                Debug.LogError(
                    $"[UIRoot] '{name}' 레이어가 비어 있습니다: {string.Join(", ", missing)}. " +
                    "인스펙터에서 해당 필드를 이 프리팹 하위의 RectTransform에 연결하세요.");
            }
        }
#endif

        /// <summary>
        /// 루트 프리팹이 지정되지 않았을 때 쓰는 기본 계층을 조립한다.
        /// 에디터의 "Create UI Root Prefab" 메뉴도 이 메서드를 재사용하므로
        /// 코드 기본값과 프리팹 템플릿이 어긋날 수 없다.
        /// 상주화(DontDestroyOnLoad)는 여기서 하지 않는다 — 에디터에서도 쓰이기 때문이다.
        /// </summary>
        public static UIRoot CreateDefault()
        {
            var go = new GameObject(
                "[UIService]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referenceResolution = DefaultReferenceResolution;

            var root = go.AddComponent<UIRoot>();

            // 생성 순서 = sibling 순서 = 렌더 순서(아래→위). Overlay는 Popup 기준 Above/Below로 분리된다.
            root._pageLayer = CreateLayer(go.transform, "[Page]");
            root._belowOverlayLayer = CreateLayer(go.transform, "[BelowOverlay]");
            root._popupLayer = CreateLayer(go.transform, "[Popup]");
            root._aboveOverlayLayer = CreateLayer(go.transform, "[AboveOverlay]");

            return root;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;

            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            return rt;
        }
    }
}
