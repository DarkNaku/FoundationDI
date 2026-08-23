using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃의 스크린 rect를 계산한다. UI(RectTransform)와 3D(Renderer/Collider)를
    /// 한 함수로 흡수해서, 모듈이 타깃 종류를 구분하지 않아도 되게 한다.
    /// </summary>
    public static class TutorialScreenRect
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        public static bool TryGet(Transform target, Camera camera, out Rect screenRect)
        {
            screenRect = default;

            if (target == null) return false;

            if (target is RectTransform rectTransform) return TryGetUI(rectTransform, out screenRect);

            if (camera == null) return false;

            if (target.TryGetComponent<Renderer>(out var renderer))
            {
                return TryGetBounds(renderer.bounds, camera, out screenRect);
            }

            if (target.TryGetComponent<Collider>(out var collider))
            {
                return TryGetBounds(collider.bounds, camera, out screenRect);
            }

            var point = camera.WorldToScreenPoint(target.position);

            screenRect = new Rect(point.x, point.y, 0f, 0f);
            return true;
        }

        private static bool TryGetUI(RectTransform rectTransform, out Rect screenRect)
        {
            screenRect = default;

            var canvas = rectTransform.GetComponentInParent<Canvas>();

            if (canvas == null) return false;

            // ScreenSpaceOverlay는 카메라가 없다. RectTransformUtility가 null 카메라를 요구한다.
            var canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            rectTransform.GetWorldCorners(Corners);

            var min = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[0]);
            var max = min;

            for (var i = 1; i < 4; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[i]);

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            screenRect = new Rect(min, max - min);
            return true;
        }

        private static bool TryGetBounds(Bounds bounds, Camera camera, out Rect screenRect)
        {
            var min = Vector2.positiveInfinity;
            var max = Vector2.negativeInfinity;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                var point = camera.WorldToScreenPoint(corner);

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            screenRect = new Rect(min, max - min);
            return true;
        }
    }
}
