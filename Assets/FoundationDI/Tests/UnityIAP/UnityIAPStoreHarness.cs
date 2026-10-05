using System;
using System.Collections.Generic;
using NSubstitute;
using UnityEngine.Purchasing;

// StoreController가 구현하는 Unity 인터페이스 셋을 하나의 대체 객체로 흉내낸다.
// 기본 동작은 실제 SDK의 순서를 따르되 이벤트를 동기로 쏜다 — 테스트가 프레임을 기다리지 않게.
// 순서를 바꿔 봐야 하는 경로(복원 조회 지연 등)는 플래그로 붙잡아 두고 테스트가 직접 쏜다.
public class UnityIAPStoreHarness
{
    public readonly IStoreService Store;
    public readonly IProductService Products;
    public readonly IPurchaseService Purchases;

    public bool ConnectSucceeds = true;
    public bool ProductsFetchSucceeds = true;
    public bool PurchasesFetchSucceeds = true;
    public bool RestoreSucceeds = true;

    // true면 RestoreTransactions가 뒤따르는 조회를 쏘지 않는다. 테스트가 RaisePurchasesFetched로 쏜다.
    public bool HoldRestoreFetch;

    // 조회(FetchPurchases)가 돌려줄 주문.
    public readonly List<ConfirmedOrder> ConfirmedOnFetch = new();
    public readonly List<PendingOrder> PendingOnFetch = new();

    public readonly List<string> Calls = new();
    public readonly List<Product> PurchaseCalls = new();
    public readonly List<PendingOrder> ConfirmCalls = new();
    public List<ProductDefinition> FetchedDefinitions;

    private readonly Dictionary<string, Product> _products = new();
    private readonly Dictionary<string, ProductDefinition> _catalog = new();

    // 카탈로그를 알아야 초기화 전에 만든 주문(재전달 구매)의 상품이 조회 결과와 같은 정의를 갖는다.
    public UnityIAPStoreHarness(IEnumerable<ProductDefinition> catalog = null)
    {
        if (catalog != null)
        {
            foreach (var definition in catalog) _catalog[definition.id] = definition;
        }

        var substitute = Substitute.For<IStoreService, IProductService, IPurchaseService>();
        Store = substitute;
        Products = (IProductService)substitute;
        Purchases = (IPurchaseService)substitute;

        Store.When(s => s.Connect()).Do(_ =>
        {
            Calls.Add("Connect");

            if (ConnectSucceeds) Store.OnStoreConnected += Raise.Event<Action>();
            else Store.OnStoreDisconnected += Raise.Event<Action<StoreConnectionFailureDescription>>(
                new StoreConnectionFailureDescription("연결 실패"));
        });

        Products.When(p => p.FetchProducts(Arg.Any<List<ProductDefinition>>(), Arg.Any<IRetryPolicy>())).Do(call =>
        {
            Calls.Add("FetchProducts");
            FetchedDefinitions = call.Arg<List<ProductDefinition>>();

            if (!ProductsFetchSucceeds)
            {
                Products.OnProductsFetchFailed += Raise.Event<Action<ProductFetchFailed>>(
                    UnityIAPTestObjects.ProductFetchFailed(FetchedDefinitions, "조회 실패"));
                return;
            }

            var fetched = new List<Product>();
            foreach (var definition in FetchedDefinitions) fetched.Add(Product(definition));

            Products.OnProductsFetched += Raise.Event<Action<List<Product>>>(fetched);
        });

        Purchases.When(p => p.FetchPurchases()).Do(_ =>
        {
            Calls.Add("FetchPurchases");
            RaisePurchasesFetched();
        });

        Purchases.When(p => p.ProcessPendingOrdersOnPurchasesFetched(Arg.Any<bool>()))
                 .Do(call => Calls.Add($"ProcessPending:{call.Arg<bool>()}"));

        Purchases.When(p => p.PurchaseProduct(Arg.Any<Product>()))
                 .Do(call => PurchaseCalls.Add(call.Arg<Product>()));

        Purchases.When(p => p.ConfirmPurchase(Arg.Any<PendingOrder>()))
                 .Do(call => ConfirmCalls.Add(call.Arg<PendingOrder>()));

        // Unity의 PurchaseService.RestoreTransactions와 같은 순서: 성공이면 FetchPurchases()를 먼저
        // 부르고 곧바로 콜백을 부른다. 실제 조회 결과는 비동기로 나중에 올 수 있다.
        Purchases.When(p => p.RestoreTransactions(Arg.Any<Action<bool, string>>())).Do(call =>
        {
            Calls.Add("Restore");
            var callback = call.Arg<Action<bool, string>>();

            if (!RestoreSucceeds)
            {
                callback?.Invoke(false, "복원 실패");
                return;
            }

            if (!HoldRestoreFetch) Purchases.FetchPurchases();
            callback?.Invoke(true, null);
        });
    }

    // 같은 ID면 같은 Product 객체를 돌려준다 — SDK도 조회한 객체를 주문에 그대로 싣는다.
    public Product Product(ProductDefinition definition)
    {
        if (_products.TryGetValue(definition.id, out var product)) return product;

        product = UnityIAPTestObjects.Product(definition);
        _products[definition.id] = product;
        return product;
    }

    public Product Product(string id)
    {
        if (_products.TryGetValue(id, out var product)) return product;

        return Product(_catalog.TryGetValue(id, out var definition)
            ? definition
            : UnityIAPTestObjects.Definition(id, ProductType.Consumable));
    }

    public PendingOrder Pending(string productId, string transactionId, string receipt = "receipt") =>
        UnityIAPTestObjects.Pending(Product(productId), transactionId, receipt);

    public ConfirmedOrder Confirmed(string productId) => UnityIAPTestObjects.Confirmed(Product(productId));

    public void RaisePending(PendingOrder order) =>
        Purchases.OnPurchasePending += Raise.Event<Action<PendingOrder>>(order);

    public void RaiseFailed(string productId, PurchaseFailureReason reason, string details = "failed") =>
        Purchases.OnPurchaseFailed += Raise.Event<Action<FailedOrder>>(
            UnityIAPTestObjects.Failed(Product(productId), reason, details));

    public void RaiseDeferred(string productId) =>
        Purchases.OnPurchaseDeferred += Raise.Event<Action<DeferredOrder>>(
            UnityIAPTestObjects.Deferred(Product(productId)));

    public void RaisePurchasesFetched()
    {
        if (PurchasesFetchSucceeds)
        {
            Purchases.OnPurchasesFetched += Raise.Event<Action<Orders>>(
                UnityIAPTestObjects.Orders(ConfirmedOnFetch, PendingOnFetch));
        }
        else
        {
            Purchases.OnPurchasesFetchFailed += Raise.Event<Action<PurchasesFetchFailureDescription>>(
                new PurchasesFetchFailureDescription(PurchasesFetchFailureReason.Unknown, "조회 실패"));
        }
    }
}
