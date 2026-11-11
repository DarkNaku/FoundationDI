using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;

// 원스토어 SDK 대역. 조회는 곧바로 답하고(Hold로 붙잡을 수 있다), 구매는 테스트가 CompletePurchase로 끝낸다.
// 소비하면 조회 목록에서 빠지고 승인하면 승인된 채 남는 스토어 동작을 흉내 낸다.
internal class FakeOneStoreClient : IOneStoreClient
{
    public readonly List<OneStoreProduct> Products = new();
    public OneStoreResult ProductsResult = OneStoreResult.Ok;
    public bool HoldProducts;

    // 구매 조회가 돌려줄 소비하지 않은 구매들.
    public readonly List<OneStorePurchase> Stored = new();
    public OneStoreResult PurchasesResult = OneStoreResult.Ok;
    public bool HoldPurchases;
    public int QueryPurchasesCount;
    private readonly List<Action<OneStoreResult, IReadOnlyList<OneStorePurchase>>> _heldQueries = new();

    public readonly List<string> PurchaseCalls = new();
    private Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> _purchaseCallback;

    public readonly List<OneStorePurchase> Consumed = new();
    public readonly List<OneStorePurchase> Acknowledged = new();

    public bool Disposed;

    public event Action<IReadOnlyList<OneStorePurchase>> PurchasesUpdated;

    public static OneStoreProduct Product(string storeId, string price = "1200", string currency = "KRW") =>
        new(storeId, storeId + " title", price, currency, long.Parse(price) * 1_000_000);

    public static OneStorePurchase Purchase(string storeId, string orderId, bool acknowledged = false,
                                            int state = OneStorePurchaseStates.Purchased) =>
        new(storeId, orderId, "token-" + orderId, state, acknowledged, "receipt-" + orderId);

    public void QueryProducts(IReadOnlyList<string> productIds,
                              Action<OneStoreResult, IReadOnlyList<OneStoreProduct>> onDone)
    {
        if (HoldProducts) return;

        onDone(ProductsResult, ProductsResult.IsSuccess ? Products.ToArray() : Array.Empty<OneStoreProduct>());
    }

    public void QueryPurchases(Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone)
    {
        QueryPurchasesCount++;

        if (HoldPurchases)
        {
            _heldQueries.Add(onDone);
            return;
        }

        AnswerQuery(onDone);
    }

    public void ReleaseQueries()
    {
        HoldPurchases = false;

        var held = _heldQueries.ToArray();
        _heldQueries.Clear();

        foreach (var onDone in held) AnswerQuery(onDone);
    }

    public void Purchase(string productId, Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone)
    {
        PurchaseCalls.Add(productId);
        _purchaseCallback = onDone;
    }

    // 결제가 끝났다. 소비하기 전까지 조회에도 실려 온다.
    public void CompletePurchase(OneStorePurchase purchase)
    {
        Stored.Add(purchase);
        Take()(OneStoreResult.Ok, new[] { purchase });
    }

    public void FailPurchase(int code, string message = "failed") =>
        Take()(new OneStoreResult(code, message), Array.Empty<OneStorePurchase>());

    public void Consume(OneStorePurchase purchase, Action<OneStoreResult> onDone)
    {
        Consumed.Add(purchase);
        Stored.RemoveAll(p => p.TransactionId == purchase.TransactionId);
        onDone(OneStoreResult.Ok);
    }

    public void Acknowledge(OneStorePurchase purchase, Action<OneStoreResult> onDone)
    {
        Acknowledged.Add(purchase);

        var index = Stored.FindIndex(p => p.TransactionId == purchase.TransactionId);
        if (index >= 0) Stored[index] = Purchase(purchase.ProductId, purchase.OrderId, acknowledged: true);

        onDone(OneStoreResult.Ok);
    }

    public void RaisePurchasesUpdated(params OneStorePurchase[] purchases) => PurchasesUpdated?.Invoke(purchases);

    public void Dispose() => Disposed = true;

    private void AnswerQuery(Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone) =>
        onDone(PurchasesResult, PurchasesResult.IsSuccess ? Stored.ToArray() : Array.Empty<OneStorePurchase>());

    private Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> Take()
    {
        var callback = _purchaseCallback ?? throw new InvalidOperationException("진행 중인 구매가 없다");
        _purchaseCallback = null;
        return callback;
    }
}
