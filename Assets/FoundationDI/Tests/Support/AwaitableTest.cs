using System;
using System.Collections;
using System.Threading;
using System.Runtime.ExceptionServices;
using UnityEngine;

// [UnityTest]의 IEnumerator와 Awaitable 사이를 잇는 테스트 전용 도우미.
//
// UniTask를 걷어내면서 필요해졌다. UniTask는 EditMode용 PlayerLoop를 스스로 설치하지만
// Awaitable은 Unity의 플레이어 루프에 의존하고, 그 루프는 플레이 중이 아닐 때 돌지 않는다.
// 실제로 EditMode에서 Awaitable.NextFrameAsync()와 WaitForSecondsAsync()는 영원히 완료되지
// 않는다(예외도 없이 그냥 멈춘다). 그래서 프레임 대기는 우리가 완료 소스를 쥐고
// EditMode에서는 EditorApplication.update로, 플레이 중에는 플레이어 루프로 깨운다.
public static class AwaitableTest
{
    private const float DefaultTimeoutSeconds = 5f;

    // async 본문을 [UnityTest]가 돌릴 수 있는 IEnumerator로 바꾼다.
    //
    // Awaitable을 직접 yield하지 않는 이유: 본문에서 던진 예외(NUnit의 AssertionException 포함)가
    // 테스트 실패로 이어져야 하는데, 그러려면 우리가 잡아서 스택을 보존한 채 다시 던져야 한다.
    public static IEnumerator Run(Func<Awaitable> body)
    {
        Exception error = null;
        var done = false;

        async void Runner()
        {
            try
            {
                await body();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                done = true;
            }
        }

        Runner();

        // yield return null은 EditMode에서도 동작한다 — 테스트 러너가 에디터 업데이트로 돌린다.
        while (!done) yield return null;

        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public static Awaitable NextFrame() => WaitUntil(Once(), DefaultTimeoutSeconds);

    public static Awaitable Delay(int milliseconds, CancellationToken cancellationToken = default)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + milliseconds / 1000.0;

        // 타임아웃을 지연보다 넉넉히 잡아 지연 자체가 타임아웃에 잘리지 않게 한다.
        return WaitUntil(() => Time.realtimeSinceStartupAsDouble >= deadline,
                         milliseconds / 1000f + DefaultTimeoutSeconds,
                         cancellationToken);
    }

    // 조건이 참이 되거나 타임아웃이 지나면 반환한다. 타임아웃에 예외를 던지지 않는 이유는
    // 기존 테스트가 UniTask.WhenAny(WaitUntil(...), Delay(3000)) 형태로 "기다려 보고 안 되면
    // 다음 단언이 말하게 한다"를 의도했기 때문이다. 그 의미를 그대로 옮긴다.
    public static Awaitable WaitUntil(Func<bool> predicate,
                                      float timeoutSeconds = DefaultTimeoutSeconds,
                                      CancellationToken cancellationToken = default)
    {
        var source = new AwaitableCompletionSource();
        var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;

        bool IsDone() => predicate() || Time.realtimeSinceStartupAsDouble >= deadline;

        // 취소는 타임아웃과 다르다 — 취소된 대기는 OperationCanceledException으로 끝나야
        // 호출부의 async 흐름이 중단된다(취소 후 뒷줄이 실행되면 안 되는 테스트가 있다).
        if (cancellationToken.IsCancellationRequested)
        {
            source.SetCanceled();
            return source.Awaitable;
        }

        if (IsDone())
        {
            source.SetResult();
            return source.Awaitable;
        }

        Pump(IsDone, source, cancellationToken);
        return source.Awaitable;
    }

    public static Awaitable Completed()
    {
        var source = new AwaitableCompletionSource();
        source.SetResult();
        return source.Awaitable;
    }

    // 첫 호출은 false, 그 다음부터 true. "한 프레임 쉬어간다"를 조건 대기로 표현한다.
    private static Func<bool> Once()
    {
        var first = true;

        return () =>
        {
            if (!first) return true;

            first = false;
            return false;
        };
    }

    private static void Pump(Func<bool> isDone, AwaitableCompletionSource source,
                             CancellationToken cancellationToken)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 플레이어 루프가 돌지 않으므로 에디터 업데이트에 얹는다.
            void Tick()
            {
                if (cancellationToken.IsCancellationRequested)
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
        PumpOnPlayerLoop(isDone, source, cancellationToken);
    }

    private static async void PumpOnPlayerLoop(Func<bool> isDone, AwaitableCompletionSource source,
                                               CancellationToken cancellationToken)
    {
        try
        {
            while (!isDone())
            {
                if (cancellationToken.IsCancellationRequested)
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
