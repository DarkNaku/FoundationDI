using System;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// arm/disarm 구독 모델의 트리거를 await 흐름에 잇는 유일한 지점.
    /// 트리거 자체를 Awaitable로 만들지 않은 이유는 IMessageService.Subscribe가 구독 모델이고,
    /// [SerializeReference] 객체라 생성자 주입이 안 되며, 테스트 검증이 호출 확인으로 끝나기 때문이다.
    /// </summary>
    internal static class TutorialTriggerAwaiter
    {
        public static Awaitable WaitAsync(ITutorialTrigger trigger,
                                          TutorialTriggerContext context,
                                          CancellationToken token)
        {
            var source = new AwaitableCompletionSource();

            if (trigger == null)
            {
                source.SetResult();
                return source.Awaitable;
            }

            if (token.IsCancellationRequested)
            {
                source.SetCanceled();
                return source.Awaitable;
            }

            var settled = false;
            CancellationTokenRegistration registration = default;

            void Settle(Action complete)
            {
                if (settled) return;

                settled = true;
                registration.Dispose();

                try
                {
                    trigger.Disarm();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                complete();
            }

            registration = token.Register(() => Settle(() => source.TrySetCanceled()));

            try
            {
                trigger.Arm(context, () => Settle(() => source.TrySetResult()));
            }
            catch (Exception e)
            {
                // Arm이 터지면 Step을 세우지 않고 즉시 통과시킨다 — 튜토리얼이 게임을 막지 않게.
                Debug.LogException(e);
                Settle(() => source.TrySetResult());
            }

            return source.Awaitable;
        }
    }
}
