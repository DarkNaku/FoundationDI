using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIRoot
    {
        public GameObject CanvasObject { get; }
        public Transform BelowOverlayLayer { get; }
        public Transform PageLayer { get; }
        public Transform PopupLayer { get; }
        public Transform AboveOverlayLayer { get; }

        public UIRoot()
        {
            CanvasObject = new GameObject("[UIManager]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = CanvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.DontDestroyOnLoad(CanvasObject);

            BelowOverlayLayer = CreateLayer("BelowOverlay");
            PageLayer = CreateLayer("Page");
            PopupLayer = CreateLayer("Popup");
            AboveOverlayLayer = CreateLayer("AboveOverlay");
        }

        private Transform CreateLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(CanvasObject.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
