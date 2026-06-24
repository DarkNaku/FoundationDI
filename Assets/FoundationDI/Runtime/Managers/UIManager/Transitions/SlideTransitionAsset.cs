using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum SlideDirection { Left, Right, Top, Bottom }

    [CreateAssetMenu(fileName = "SlideTransition", menuName = "DarkNaku/UI Transition/Slide")]
    public sealed class SlideTransitionAsset : UITransitionAsset
    {
        [SerializeField] private SlideDirection _direction = SlideDirection.Bottom;

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

        public override UniTask PlayShow(RectTransform target, CancellationToken ct)
        {
            var home = target.anchoredPosition;
            var off = home + OffsetFor(target);
            return Animate(t => target.anchoredPosition = Vector2.Lerp(off, home, t), ct);
        }

        public override UniTask PlayHide(RectTransform target, CancellationToken ct)
        {
            var home = target.anchoredPosition;
            var off = home + OffsetFor(target);
            return Animate(t => target.anchoredPosition = Vector2.Lerp(home, off, t), ct);
        }
    }
}
