using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UITransitionBehaviour : MonoBehaviour, IUITransition
    {
        [SerializeField] protected float _duration = 0.2f;
        [SerializeField] protected AnimationCurve _ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] protected bool _unscaledTime = true;

        public abstract UniTask ShowAsync(RectTransform target, CancellationToken ct);
        public abstract UniTask HideAsync(RectTransform target, CancellationToken ct);

        // 트윈 라이브러리 없이 매 프레임 보간
        protected async UniTask Animate(Action<float> apply, CancellationToken ct)
        {
            if (_duration <= 0f)
            {
                apply(1f);
                return;
            }

            var elapsed = 0f;
            apply(0f);

            while (elapsed < _duration)
            {
                if (ct.IsCancellationRequested)
                {
                    apply(1f);
                    return;
                }

                // ct를 Yield에 넘기지 않는다: 취소 시 예외를 던지는 대신 루프 상단 체크에서
                // apply(1f) 후 정상 종료해, 취소 경로에서도 항상 "끝 상태로 마감"을 보장한다.
                await UniTask.Yield(PlayerLoopTiming.Update);
                elapsed += _unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / _duration);
                apply(_ease.Evaluate(t));
            }

            apply(1f);
        }
    }
}
