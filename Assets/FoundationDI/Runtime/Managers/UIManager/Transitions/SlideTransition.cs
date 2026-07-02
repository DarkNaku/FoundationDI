using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // SlideDirection은 SlideDirection.cs에 정의됨.
    [AddComponentMenu("DarkNaku/UI Transition/Slide")]
    public sealed class SlideTransition : UITransitionBehaviour
    {
        [SerializeField] private CanvasGroup _background;
        [SerializeField] private RectTransform _content;
        [SerializeField] private SlideDirection _direction = SlideDirection.Bottom;

        private RectTransform Content(RectTransform root) => _content != null ? _content : root;

        private Vector2 OffsetFor(RectTransform target)
        {
            var size = target.rect.size;
            return _direction switch
            {
                SlideDirection.Left => new Vector2(-size.x, 0),
                SlideDirection.Right => new Vector2(size.x, 0),
                SlideDirection.Top => new Vector2(0, size.y),
                _ => new Vector2(0, -size.y),
            };
        }

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var home = content.anchoredPosition;
            var off = home + OffsetFor(content);
            var slide = Animate(t => content.anchoredPosition = Vector2.Lerp(off, home, t), ct);
            if (_background == null) return slide;
            var fade = Animate(t => _background.alpha = t, ct);
            return UniTask.WhenAll(slide, fade);
        }

        public override async UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var home = content.anchoredPosition;
            var off = home + OffsetFor(content);
            var slide = Animate(t => content.anchoredPosition = Vector2.Lerp(home, off, t), ct);
            if (_background == null)
            {
                await slide;
            }
            else
            {
                var fade = Animate(t => _background.alpha = 1f - t, ct);
                await UniTask.WhenAll(slide, fade);
            }
            // 휴지 위치를 home으로 복원(캐시 재사용 시 다음 Show가 화면 밖 좌표를 home으로 캡처하는 것 방지).
            content.anchoredPosition = home;
        }
    }
}
