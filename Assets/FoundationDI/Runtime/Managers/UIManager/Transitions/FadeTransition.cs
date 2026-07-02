using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [AddComponentMenu("DarkNaku/UI Transition/Fade")]
    public sealed class FadeTransition : UITransitionBehaviour
    {
        [SerializeField] private CanvasGroup _target;

        private CanvasGroup Resolve(RectTransform root)
            => _target != null ? _target : root.GetComponent<CanvasGroup>();

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var cg = Resolve(target);
            return Animate(t => cg.alpha = t, ct);
        }

        public override UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var cg = Resolve(target);
            return Animate(t => cg.alpha = 1f - t, ct);
        }
    }
}
