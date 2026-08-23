using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using UnityEngine;

// 정책 계층 테스트용 가짜 스토어. 이벤트는 테스트가 직접 쏜다 —
// 실제 SDK의 타이밍을 흉내내지 않아야 어떤 순서든 재현할 수 있다.
public class FakeIapProvider : IIapProvider
{
    private readonly List<IapProduct> _products = new();
    private int _sequence;

    public string Name => "Fake";

    public bool InitializeResult = true;
    public int InitializeCount;
    public bool PurchaseResult = true;
    public bool RestoreResult = true;
    public int RestoreCount;
    public bool IsDisposed;

    public readonly List<string> PurchaseCalls = new();
    public readonly List<string> ConfirmCalls = new();

    // InitializeAsync 도중(구독 이후) 발행할 미확정 구매. Unity IAP의 FetchPurchases 재전달을 흉내낸다.
    public readonly List<IapPendingPurchase> PendingOnInitialize = new();

    // RestoreAsync 도중 발행할 복원 구매.
    public readonly List<IapPendingPurchase> PendingOnRestore = new();

    public IReadOnlyList<IapProduct> Products => _products;

    public Awaitable<bool> InitializeAsync(IapProviderContext context)
    {
        InitializeCount++;

        if (InitializeResult && context.Products != null)
        {
            foreach (var definition in context.Products)
            {
                _products.Add(new IapProduct(definition.Id, definition.StoreId, definition.Type,
                    $"{definition.Id} (Fake)", "fake product", "$0.99", 0.99, "USD", true));
            }
        }

        // 재전달은 연결 직후에 온다 — 구독이 먼저 끝났는지 검증하는 것이 이 순서의 목적이다.
        if (InitializeResult)
        {
            foreach (var pending in PendingOnInitialize) PurchasePending?.Invoke(pending);
        }

        return Completed(InitializeResult);
    }

    public bool Purchase(string storeId)
    {
        PurchaseCalls.Add(storeId);
        return PurchaseResult;
    }

    public void Confirm(string transactionId) => ConfirmCalls.Add(transactionId);

    public Awaitable<bool> RestoreAsync()
    {
        RestoreCount++;

        if (RestoreResult)
        {
            foreach (var pending in PendingOnRestore) PurchasePending?.Invoke(pending);
        }

        return Completed(RestoreResult);
    }

    public void Dispose() => IsDisposed = true;

    public string NextTransactionId(string storeId) => $"fake-{storeId}-{_sequence++}";

    public void RaisePending(IapPendingPurchase pending) => PurchasePending?.Invoke(pending);
    public void RaiseFailed(IapPurchaseFailure failure) => PurchaseFailed?.Invoke(failure);
    public void RaiseDeferred(string storeId) => PurchaseDeferred?.Invoke(storeId);

    public event Action<IapPendingPurchase> PurchasePending;
    public event Action<IapPurchaseFailure> PurchaseFailed;
    public event Action<string> PurchaseDeferred;

    private static Awaitable<bool> Completed(bool value)
    {
        var source = new AwaitableCompletionSource<bool>();
        source.SetResult(value);
        return source.Awaitable;
    }
}

public class FakeFulfillment : IIapFulfillment
{
    public readonly List<IapPurchase> Calls = new();
    public bool Result = true;
    public bool Throw;

    public Awaitable<bool> FulfillAsync(IapPurchase purchase)
    {
        Calls.Add(purchase);

        if (Throw) throw new InvalidOperationException("지급 실패 시뮬레이션");

        var source = new AwaitableCompletionSource<bool>();
        source.SetResult(Result);
        return source.Awaitable;
    }
}

public class FakeReceiptValidator : IReceiptValidator
{
    public bool Result = true;
    public int CallCount;

    public bool Validate(IapPurchase purchase, out IapError error)
    {
        CallCount++;
        error = Result ? default : new IapError(-1, "위조된 영수증");
        return Result;
    }
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
