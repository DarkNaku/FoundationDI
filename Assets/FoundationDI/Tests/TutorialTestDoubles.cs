using System;
using System.Collections.Generic;
using System.Threading;
using DarkNaku.FoundationDI;
using UnityEngine;

/// <summary>
/// 지연은 기록만 하고 즉시 끝내되, 프레임 대기는 진짜로 한 프레임 양보한다.
/// 프레임 대기까지 즉시 끝내면 무한 타임아웃 폴링 루프가 제어를 절대 반환하지 않아
/// 테스트가 그대로 멈춘다.
/// </summary>
public sealed class FakeClock : ITutorialClock
{
    public float TotalDelay { get; private set; }
    public int FrameCount { get; private set; }

    public Awaitable DelayAsync(float seconds, CancellationToken token)
    {
        TotalDelay += seconds;

        var source = new AwaitableCompletionSource();

        if (token.IsCancellationRequested) source.SetCanceled();
        else source.SetResult();

        return source.Awaitable;
    }

    public Awaitable NextFrameAsync(CancellationToken token)
    {
        FrameCount++;

        var first = true;

        return AwaitableTest.WaitUntil(() =>
        {
            if (!first) return true;

            first = false;
            return false;
        }, 5f, token);
    }
}

public sealed class FakeTrigger : ITutorialTrigger
{
    private Action _onFired;

    public int ArmCount { get; private set; }
    public int DisarmCount { get; private set; }
    public bool IsArmed => _onFired != null;
    public TutorialTriggerContext LastContext { get; private set; }

    /// <summary>Arm 시 예외를 던지게 하려면 true.</summary>
    public bool ThrowOnArm { get; set; }

    public void Arm(TutorialTriggerContext context, Action onFired)
    {
        ArmCount++;
        LastContext = context;

        if (ThrowOnArm) throw new InvalidOperationException("arm failed");

        _onFired = onFired;
    }

    public void Disarm()
    {
        DisarmCount++;
        _onFired = null;
    }

    public void Fire() => _onFired?.Invoke();
}

public sealed class FakeModule : ITutorialModule
{
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public TutorialTargetHandle LastTarget { get; private set; }
    public bool ThrowOnShow { get; set; }
    public bool ThrowOnHide { get; set; }

    /// <summary>호출 순서를 시퀀스 단위로 관찰하려면 여기에 로그를 공유시킨다.</summary>
    public List<string> Log { get; set; }

    public string Name { get; set; } = "module";

    public Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token)
    {
        ShowCount++;
        LastTarget = target;
        Log?.Add($"{Name}.show");

        if (ThrowOnShow) throw new InvalidOperationException("show failed");

        return Completed();
    }

    public Awaitable HideAsync(CancellationToken token)
    {
        HideCount++;
        Log?.Add($"{Name}.hide");

        if (ThrowOnHide) throw new InvalidOperationException("hide failed");

        return Completed();
    }

    private static Awaitable Completed()
    {
        var source = new AwaitableCompletionSource();
        source.SetResult();
        return source.Awaitable;
    }
}

/// <summary>오써링 테스트용 최소 모듈. 추적 콜백은 호출만 세고 아무것도 그리지 않는다.</summary>
public sealed class FakeModuleBehaviour : TutorialModuleBehaviour
{
    public int TrackCount { get; private set; }
    public int LostCount { get; private set; }

    protected override void OnTrack(Rect screenRect) => TrackCount++;

    protected override void OnTargetLost() => LostCount++;
}

public sealed class FakeProgressStorage : ITutorialProgressStorage
{
    private readonly Dictionary<string, TutorialState> _states = new();
    private readonly Dictionary<string, int> _steps = new();

    public bool AllSkipped { get; set; }

    public TutorialState GetState(string sequenceId) =>
        _states.TryGetValue(sequenceId, out var s) ? s : TutorialState.NotStarted;

    public void SetState(string sequenceId, TutorialState state) => _states[sequenceId] = state;

    public int GetStepIndex(string sequenceId) =>
        _steps.TryGetValue(sequenceId, out var i) ? i : 0;

    public void SetStepIndex(string sequenceId, int index) => _steps[sequenceId] = index;

    public void Clear()
    {
        _states.Clear();
        _steps.Clear();
        AllSkipped = false;
    }
}

public sealed class FakeTargetRegistry : ITutorialTargetRegistry
{
    private readonly Dictionary<string, Transform> _targets = new();
    private readonly List<TutorialTargetHandle> _handles = new();

    /// <summary>true면 ResolveAsync가 타임아웃된 것처럼 null을 돌려준다.</summary>
    public bool FailResolve { get; set; }

    public int ResolveCount { get; private set; }

    public void Register(string key, Transform target)
    {
        _targets[key] = target;

        foreach (var handle in _handles) handle.SetCurrent(target);
    }

    public void Unregister(string key, Transform target)
    {
        if (!_targets.TryGetValue(key, out var current)) return;
        if (!ReferenceEquals(current, target)) return;

        _targets.Remove(key);

        foreach (var handle in _handles) handle.SetCurrent(null);
    }

    public bool TryResolve(TutorialTargetRef reference, out Transform target)
    {
        if (reference.Direct != null)
        {
            target = reference.Direct;
            return true;
        }

        if (reference.HasKey) return _targets.TryGetValue(reference.Key, out target);

        target = null;
        return false;
    }

    public Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                        float timeoutSeconds,
                                                        CancellationToken token)
    {
        ResolveCount++;

        var source = new AwaitableCompletionSource<TutorialTargetHandle>();

        if (token.IsCancellationRequested)
        {
            source.SetCanceled();
            return source.Awaitable;
        }

        if (FailResolve)
        {
            source.SetResult(null);
            return source.Awaitable;
        }

        TryResolve(reference, out var target);

        var handle = new TutorialTargetHandle(target);
        _handles.Add(handle);
        source.SetResult(handle);

        return source.Awaitable;
    }
}
