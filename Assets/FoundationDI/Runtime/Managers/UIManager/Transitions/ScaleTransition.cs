using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    [AddComponentMenu("DarkNaku/UI Transition/Scale")]
    public sealed class ScaleTransition : UITransitionBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private RectTransform _content;
        [SerializeField] private float _fromScale = 0.8f;

        // 배경 Image의 디자인 알파(휴지 상태)를 최초 1회 기억해 그 값까지 페이드한다.
        private float _bgAlpha;
        private bool _bgCaptured;

        private void CaptureBackground()
        {
            if (_bgCaptured || _background == null) return;
            _bgAlpha = _background.color.a;
            _bgCaptured = true;
        }

        private void SetBackgroundAlpha(float a)
        {
            var c = _background.color;
            c.a = a;
            _background.color = c;
        }

        private RectTransform Content(RectTransform root) => _content != null ? _content : root;

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            CaptureBackground();
            var content = Content(target);
            var scale = Animate(t => content.localScale = Vector3.one * Mathf.Lerp(_fromScale, 1f, t), ct);
            if (_background == null) return scale;
            var fade = Animate(t => SetBackgroundAlpha(_bgAlpha * t), ct);
            return UniTask.WhenAll(scale, fade);
        }

        public override async UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            CaptureBackground();
            var content = Content(target);
            var scale = Animate(t => content.localScale = Vector3.one * Mathf.Lerp(1f, _fromScale, t), ct);
            if (_background == null)
            {
                await scale;
                return;
            }
            var fade = Animate(t => SetBackgroundAlpha(_bgAlpha * (1f - t)), ct);
            await UniTask.WhenAll(scale, fade);
        }
    }
}
