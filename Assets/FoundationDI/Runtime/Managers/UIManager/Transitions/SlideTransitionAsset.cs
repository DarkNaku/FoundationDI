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

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var home = target.anchoredPosition;
            var off = home + OffsetFor(target);
            return Animate(t => target.anchoredPosition = Vector2.Lerp(off, home, t), ct);
        }

        public override async UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var home = target.anchoredPosition;
            var off = home + OffsetFor(target);
            await Animate(t => target.anchoredPosition = Vector2.Lerp(home, off, t), ct);
            // 휴지 위치를 home으로 복원한다. 그대로 두면 캐시 재사용 시 다음 ShowAsync가
            // 화면 밖 좌표를 home으로 캡처해 원위치로 복귀하지 못한다. (직후 SetActive(false)되어 보이지 않음)
            target.anchoredPosition = home;
        }
    }
}
