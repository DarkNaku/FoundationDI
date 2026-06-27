using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIRoot
    {
        public GameObject GO { get; }
        public Transform PageLayer { get; }
        public Transform BelowOverlayLayer { get; }
        public Transform PopupLayer { get; }
        public Transform AboveOverlayLayer { get; }

        public UIRoot(Vector2 referenceResolution = default)
        {
            GO = new GameObject("[UIManager]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = GO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referenceResolution = (referenceResolution.x > 0f && referenceResolution.y > 0f)
                ? referenceResolution
                : new Vector2(1920f, 1080f);

            Object.DontDestroyOnLoad(GO);

            // 생성 순서 = sibling 순서 = 렌더 순서(아래→위). Overlay는 Popup 기준 Above/Below로 분리된다.
            PageLayer = CreateLayer("[Page]");
            BelowOverlayLayer = CreateLayer("[BelowOverlay]");
            PopupLayer = CreateLayer("[Popup]");
            AboveOverlayLayer = CreateLayer("[AboveOverlay]");
        }

        private Transform CreateLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;

            rt.SetParent(GO.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            return rt;
        }
    }
}
