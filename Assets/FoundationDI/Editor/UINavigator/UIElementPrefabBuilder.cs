using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>모드별 UI 프리팹 계층을 조립한다. 저장은 호출자 책임이다.</summary>
    public static class UIElementPrefabBuilder
    {
        public static GameObject Build(Type viewType, UIElementMode mode)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));

            if (!typeof(UIView).IsAssignableFrom(viewType))
            {
                throw new ArgumentException($"'{viewType.Name}'은(는) UIView 파생 타입이 아니다.", nameof(viewType));
            }

            var go = new GameObject(viewType.Name, typeof(RectTransform), typeof(CanvasGroup));

            Stretch((RectTransform)go.transform);

            // UIView는 [RequireComponent(typeof(CanvasGroup))]라 CanvasGroup이 먼저 있어야 한다.
            go.AddComponent(viewType);

            if (mode == UIElementMode.Popup) BuildPopupChildren(go.transform);

            return go;
        }

        private static void BuildPopupChildren(Transform parent)
        {
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));

            background.transform.SetParent(parent, false);
            Stretch((RectTransform)background.transform);

            var image = background.GetComponent<Image>();

            image.color = new Color(0f, 0f, 0f, 0.5f);
            image.raycastTarget = true; // 모달: 뒤쪽 입력 차단

            var content = new GameObject("Content", typeof(RectTransform));

            content.transform.SetParent(parent, false);

            var rt = (RectTransform)content.transform;

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(800f, 600f);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
