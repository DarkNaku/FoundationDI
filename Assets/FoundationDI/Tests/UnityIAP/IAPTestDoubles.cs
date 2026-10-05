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
