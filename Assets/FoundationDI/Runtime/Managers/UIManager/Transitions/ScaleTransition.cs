using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [AddComponentMenu("DarkNaku/UI Transition/Scale")]
    public sealed class ScaleTransition : UITransitionBehaviour
    {
        [SerializeField] private CanvasGroup _background;
        [SerializeField] private RectTransform _content;
        [SerializeField] private float _fromScale = 0.8f;

        private RectTransform Content(RectTransform root) => _content != null ? _content : root;

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var scale = Animate(t => content.localScale = Vector3.one * Mathf.Lerp(_fromScale, 1f, t), ct);
            if (_background == null) return scale;
            var fade = Animate(t => _background.alpha = t, ct);
            return UniTask.WhenAll(scale, fade);
        }

        public override async UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var scale = Animate(t => content.localScale = Vector3.one * Mathf.Lerp(1f, _fromScale, t), ct);
            if (_background == null)
            {
                await scale;
                return;
            }
            var fade = Animate(t => _background.alpha = 1f - t, ct);
            await UniTask.WhenAll(scale, fade);
        }
    }
}
