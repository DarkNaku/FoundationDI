using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UITransitionAsset : ScriptableObject, IUITransition
    {
        [SerializeField] protected float _duration = 0.2f;
        [SerializeField] protected AnimationCurve _ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] protected bool _unscaledTime = true;

        public abstract UniTask ShowAsync(RectTransform target, CancellationToken ct);
        public abstract UniTask HideAsync(RectTransform target, CancellationToken ct);

        // 트윈 라이브러리 없이 매 프레임 보간
        protected async UniTask Animate(Action<float> apply, CancellationToken ct)
        {
            if (_duration <= 0f) { apply(1f); return; }

            var elapsed = 0f;
            apply(0f);
            while (elapsed < _duration)
            {
                if (ct.IsCancellationRequested) { apply(1f); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += _unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / _duration);
                apply(_ease.Evaluate(t));
            }
            apply(1f);
        }

        // UIView에 RequireComponent(CanvasGroup)가 부착되어 트랜지션 대상에는 항상 CanvasGroup이 존재한다.
        protected static CanvasGroup GetCanvasGroup(RectTransform target) => target.GetComponent<CanvasGroup>();
    }
}
