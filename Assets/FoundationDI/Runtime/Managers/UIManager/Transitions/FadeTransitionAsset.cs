using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "FadeTransition", menuName = "DarkNaku/UI Transition/Fade")]
    public sealed class FadeTransitionAsset : UITransitionAsset
    {
        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var cg = GetCanvasGroup(target);
            return Animate(t => cg.alpha = t, ct);
        }

        public override UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var cg = GetCanvasGroup(target);
            return Animate(t => cg.alpha = 1f - t, ct);
        }
    }
}
