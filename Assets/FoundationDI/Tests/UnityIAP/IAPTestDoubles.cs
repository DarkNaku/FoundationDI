using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using UnityEngine;

public class FakeFulfillment : IIAPFulfillment
{
    public readonly List<IAPPurchase> Calls = new();
    public bool Result = true;
    public bool Throw;

    public Awaitable<bool> FulfillAsync(IAPPurchase purchase)
    {
        Calls.Add(purchase);

        if (Throw) throw new InvalidOperationException("지급 실패 시뮬레이션");

        var source = new AwaitableCompletionSource<bool>();
        source.SetResult(Result);
        return source.Awaitable;
    }
}

// 진행 데이터가 늦게 준비되는 게임의 지급 처리기.
public class GatedFulfillment : FakeFulfillment, IIAPFulfillmentGate
{
    public bool IsReady { get; set; }
}

public class FakeReceiptValidator : IReceiptValidator
{
    public bool Result = true;
    public int CallCount;

    public bool Validate(IAPPurchase purchase, out IAPError error)
    {
        CallCount++;
        error = Result ? default : new IAPError(-1, "위조된 영수증");
        return Result;
    }
}

public class FakeAsyncReceiptValidator : IAsyncReceiptValidator
{
    public readonly List<IAPPurchase> Calls = new();
    public IAPValidationResult Result = IAPValidationResult.Valid();
    public bool Throw;

    // true면 테스트가 Release로 결과를 줄 때까지 붙잡아 둔다(서버 응답 대기).
    public bool Hold;
    private AwaitableCompletionSource<IAPValidationResult> _held;

    public Awaitable<IAPValidationResult> ValidateAsync(IAPPurchase purchase)
    {
        Calls.Add(purchase);

        if (Throw) throw new InvalidOperationException("서버 검증 실패 시뮬레이션");

        var source = new AwaitableCompletionSource<IAPValidationResult>();

        if (Hold) _held = source;
        else source.SetResult(Result);

        return source.Awaitable;
    }

    public void Release(IAPValidationResult result) => _held?.SetResult(result);
}

public class FakeEntitlementStorage : IEntitlementStorage
{
    public readonly HashSet<string> Owned = new();

    public bool IsOwned(string productId) => Owned.Contains(productId);

    public void SetOwned(string productId, bool owned)
    {
        if (owned) Owned.Add(productId);
        else Owned.Remove(productId);
    }
}

// 시간 제한 테스트용 시계. Advance로 손으로 돌린다(EditMode에서는 실제 지연이 끝나지 않는다).
public class ManualIAPDispatcher : IMainThreadDispatcher
{
    private class Entry : IDisposable
    {
        public float DueAt;
        public Action Action;
        public bool Cancelled;

        public void Dispose() => Cancelled = true;
    }

    private readonly List<Entry> _entries = new();
    private float _now;

    public int ActiveCount => _entries.FindAll(e => !e.Cancelled).Count;

    public void Post(Action action) => action?.Invoke();

    public IDisposable Delay(float seconds, Action action)
    {
        var entry = new Entry { DueAt = _now + seconds, Action = action };
        _entries.Add(entry);
        return entry;
    }

    public IDisposable NextFrames(int count, Action action) => throw new NotSupportedException();

    public void Advance(float seconds)
    {
        _now += seconds;

        foreach (var entry in _entries.ToArray())
        {
            if (entry.Cancelled || entry.DueAt > _now) continue;

            entry.Cancelled = true;
            entry.Action?.Invoke();
        }
    }
}
