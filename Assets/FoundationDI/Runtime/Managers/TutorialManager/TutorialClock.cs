using System;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 프로덕션 시계. 플레이 중에는 플레이어 루프로, 에디터에서는 EditorApplication.update로 펌프한다.
    /// (Awaitable.NextFrameAsync는 플레이 중이 아닐 때 영원히 완료되지 않는다.)
    /// </summary>
    public sealed class TutorialClock : ITutorialClock
    {
        public Awaitable DelayAsync(float seconds, CancellationToken token)
        {
            if (seconds <= 0f) return WaitUntil(() => true, token);

            var deadline = Time.realtimeSinceStartupAsDouble + seconds;

            return WaitUntil(() => Time.realtimeSinceStartupAsDouble >= deadline, token);
        }

        public Awaitable NextFrameAsync(CancellationToken token)
        {
            var first = true;

            return WaitUntil(() =>
            {
                if (!first) return true;

                first = false;
                return false;
            }, token);
        }

        private static Awaitable WaitUntil(Func<bool> isDone, CancellationToken token)
        {
            var source = new AwaitableCompletionSource();

            if (token.IsCancellationRequested)
            {
                source.SetCanceled();
                return source.Awaitable;
            }

            if (isDone())
            {
                source.SetResult();
                return source.Awaitable;
            }

            Pump(isDone, source, token);

            return source.Awaitable;
        }

        private static void Pump(Func<bool> isDone, AwaitableCompletionSource source,
                                 CancellationToken token)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // 플레이어 루프가 돌지 않으므로 에디터 업데이트에 얹는다.
                void Tick()
                {
                    if (token.IsCancellationRequested)
                    {
                        UnityEditor.EditorApplication.update -= Tick;
                        source.TrySetCanceled();
                        return;
                    }

                    if (!isDone()) return;

                    UnityEditor.EditorApplication.update -= Tick;
                    source.TrySetResult();
                }

                UnityEditor.EditorApplication.update += Tick;
                return;
            }
#endif
            PumpOnPlayerLoop(isDone, source, token);
        }

        private static async void PumpOnPlayerLoop(Func<bool> isDone,
                                                   AwaitableCompletionSource source,
                                                   CancellationToken token)
        {
            try
            {
                while (!isDone())
                {
                    if (token.IsCancellationRequested)
                    {
                        source.TrySetCanceled();
                        return;
                    }

                    await Awaitable.NextFrameAsync();
                }

                source.TrySetResult();
            }
            catch (Exception e)
            {
                source.TrySetException(e);
            }
        }
    }
}
