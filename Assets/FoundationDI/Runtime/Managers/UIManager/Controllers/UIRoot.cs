using System;
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

        public UIRoot(
            Vector2 referenceResolution = default,
            string sortingLayerName = "Default",
            int sortingOrder = 0,
            float planeDistance = 100f,
            Func<Camera> cameraProvider = null)
        {
            GO = new GameObject("[UIManager]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = GO.GetComponent<Canvas>();

            var scaler = GO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referenceResolution = (referenceResolution.x > 0f && referenceResolution.y > 0f)
                ? referenceResolution
                : new Vector2(1920f, 1080f);

            // DontDestroyOnLoad를 하지 않는다 → GO는 생성 시점의 active 씬에 소속되어
            // 그 씬의 카메라(Screen Space - Camera)와 함께 수명을 같이한다.
            var camera = cameraProvider != null ? cameraProvider() : Camera.main;
            if (camera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = planeDistance;
                canvas.sortingLayerID = SortingLayer.NameToID(sortingLayerName);
                canvas.sortingOrder = sortingOrder;
            }
            else
            {
                // 로딩 화면 등 MainCamera 태그 카메라가 없는 순간엔 최상단 Overlay로 폴백.
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Debug.LogWarning("[UIManager] Camera.main이 없어 UI Canvas를 ScreenSpaceOverlay로 폴백합니다. Sorting Layer 정렬이 적용되지 않습니다.");
            }

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
