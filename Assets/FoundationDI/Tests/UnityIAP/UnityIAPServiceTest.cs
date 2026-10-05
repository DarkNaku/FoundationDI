using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.TestTools;

public class UnityIAPServiceTest
{
    private static readonly ProductDefinition[] Catalog =
    {
        UnityIAPTestObjects.Definition("gems", ProductType.Consumable, "gems_store"),
        UnityIAPTestObjects.Definition("remove_ads", ProductType.NonConsumable, "remove_ads_store"),
    };

    private static UnityIAPService NewService(UnityIAPStoreHarness store,
                                              FakeFulfillment fulfillment = null,
                                              FakeReceiptValidator validator = null,
                                              FakeEntitlementStorage entitlements = null)
    {
        return new UnityIAPService(store.Store, store.Products, store.Purchases, Catalog,
                                   fulfillment ?? new FakeFulfillment(),
                                   validator ?? new FakeReceiptValidator(),
                                   entitlements ?? new FakeEntitlementStorage(),
                                   verboseLogging: false);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구독을_끝낸_뒤_연결_상품_조회_구매_조회_순서로_초기화한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);

        // 하네스는 Connect 안에서 OnStoreConnected를 동기로 쏜다 — 구독이 늦으면 초기화가 끝나지 않는다.
        Assert.IsTrue(await sut.InitializeAsync());

        CollectionAssert.AreEqual(
            new[] { "ProcessPending:False", "Connect", "FetchProducts", "FetchPurchases" }, store.Calls);
        CollectionAssert.AreEqual(new[] { "gems", "remove_ads" }, store.FetchedDefinitions.Select(d => d.id).ToArray());
        CollectionAssert.AreEqual(new[] { "gems_store", "remove_ads_store" },
                                  store.FetchedDefinitions.Select(d => d.storeSpecificId).ToArray());
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화하면_조회된_상품이_노출된다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);

        await sut.InitializeAsync();

        Assert.IsTrue(sut.IsInitialized);
        Assert.AreEqual(2, sut.Products.Count);
        Assert.IsTrue(sut.TryGetProduct("gems", out var gems));
        Assert.AreEqual("gems_store", gems.StoreId);
        Assert.AreEqual(IAPProductType.Consumable, gems.Type);
        Assert.AreEqual("$0.99", gems.LocalizedPrice);
        Assert.AreEqual(0.99, gems.Price, 0.0001);
        Assert.AreEqual("USD", gems.CurrencyCode);
        Assert.IsTrue(gems.IsAvailable);
        Assert.IsTrue(sut.TryGetProduct("remove_ads", out var removeAds));
        Assert.AreEqual(IAPProductType.NonConsumable, removeAds.Type);
        Assert.IsFalse(sut.TryGetProduct("unknown", out _));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 연결이_끊기면_초기화가_실패한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { ConnectSucceeds = false };
        var sut = NewService(store);

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));
        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        Assert.IsFalse(await sut.InitializeAsync());
        Assert.IsFalse(sut.IsInitialized);
        Assert.IsEmpty(sut.Products);
        CollectionAssert.DoesNotContain(store.Calls, "FetchProducts");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 상품_조회가_실패하면_초기화가_실패한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { ProductsFetchSucceeds = false };
        var sut = NewService(store);

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*조회"));
        LogAssert.Expect(LogType.Error, new Regex("IAPService.*초기화"));

        Assert.IsFalse(await sut.InitializeAsync());
        Assert.IsFalse(sut.IsInitialized);
        CollectionAssert.DoesNotContain(store.Calls, "FetchPurchases");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매_조회가_실패해도_초기화는_성공한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { PurchasesFetchSucceeds = false };
        var sut = NewService(store);

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        // 미확정 구매를 이번 실행에 못 찾을 뿐이다 — 상품을 사는 데는 지장이 없다.
        Assert.IsTrue(await sut.InitializeAsync());
        Assert.IsTrue(sut.IsInitialized);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);

        Assert.AreEqual(IAPPurchaseOutcome.NotReady, (await sut.PurchaseAsync("gems")).Outcome);
        Assert.IsEmpty(store.PurchaseCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator InitializeAsync는_재진입해도_한_번만_연결한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);

        var first = sut.InitializeAsync();
        var second = sut.InitializeAsync();

        Assert.IsTrue(await first);
        Assert.IsTrue(await second);
        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, store.Calls.Count(c => c == "Connect"));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매가_검증_지급_확정_순서로_진행된다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var validator = new FakeReceiptValidator();
        var sut = NewService(store, fulfillment, validator);
        await sut.InitializeAsync();

        var published = new System.Collections.Generic.List<IAPPurchase>();
        sut.Purchased += p => published.Add(p);

        var task = sut.PurchaseAsync("gems");

        Assert.AreEqual(1, store.PurchaseCalls.Count);
        Assert.AreSame(store.Product("gems"), store.PurchaseCalls[0], "조회한 Product로 사야 한다");

        var order = store.Pending("gems", "tx-1", "receipt-1");
        store.RaisePending(order);
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Purchased, result.Outcome);
        Assert.AreEqual("gems", result.Purchase.ProductId);
        Assert.AreEqual("tx-1", result.Purchase.TransactionId);
        Assert.AreEqual("receipt-1", result.Purchase.Receipt);
        Assert.AreEqual(0.99, result.Purchase.Price, 0.0001);
        Assert.AreEqual("USD", result.Purchase.CurrencyCode);
        Assert.IsFalse(result.Purchase.IsRestored);

        Assert.AreEqual(1, validator.CallCount);
        Assert.AreEqual(1, fulfillment.Calls.Count);
        CollectionAssert.AreEqual(new[] { order }, store.ConfirmCalls, "지급 후 같은 주문을 확정해야 한다");
        Assert.AreEqual(1, published.Count, "확정 후 Purchased 이벤트가 발행되지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급이_실패하면_확정하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store, new FakeFulfillment { Result = false });
        await sut.InitializeAsync();

        var published = 0;
        sut.Purchased += _ => published++;

        var task = sut.PurchaseAsync("remove_ads");
        store.RaisePending(store.Pending("remove_ads", "tx-2"));
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.IsEmpty(store.ConfirmCalls, "지급에 실패했는데 확정했다");
        Assert.IsFalse(sut.IsOwned("remove_ads"), "지급에 실패했는데 소유로 기록했다");
        Assert.AreEqual(0, published);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급이_예외를_던져도_확정하지_않고_서비스가_살아있다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment { Throw = true };
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-3"));
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.IsEmpty(store.ConfirmCalls);

        // 다음 구매가 정상으로 흐른다.
        fulfillment.Throw = false;
        var retry = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-4"));
        Assert.AreEqual(IAPPurchaseOutcome.Purchased, (await retry).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 영수증_검증에_실패하면_지급도_확정도_하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment, new FakeReceiptValidator { Result = false });
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-5", "bad"));
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.InvalidReceipt, result.Outcome);
        Assert.AreEqual(-1, result.Error.Code);
        Assert.IsEmpty(fulfillment.Calls, "검증에 실패했는데 지급했다");
        Assert.IsEmpty(store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비소모성은_확정_후_소유로_기록되고_소모성은_아니다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();

        string ownedChanged = null;
        sut.OwnedChanged += id => ownedChanged = id;

        var nonConsumable = sut.PurchaseAsync("remove_ads");
        store.RaisePending(store.Pending("remove_ads", "tx-6"));
        await nonConsumable;

        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.AreEqual("remove_ads", ownedChanged);

        var consumable = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-7"));
        await consumable;

        Assert.IsFalse(sut.IsOwned("gems"), "소모성이 소유로 기록됐다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 사용자_취소와_그_외_실패를_구분한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();

        var cancelling = sut.PurchaseAsync("gems");
        store.RaiseFailed("gems", PurchaseFailureReason.UserCancelled);
        Assert.AreEqual(IAPPurchaseOutcome.UserCancelled, (await cancelling).Outcome);

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        var failing = sut.PurchaseAsync("gems");
        store.RaiseFailed("gems", PurchaseFailureReason.PaymentDeclined, "declined");
        var result = await failing;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual((int)PurchaseFailureReason.PaymentDeclined, result.Error.Code);
        StringAssert.Contains("declined", result.Error.Message);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 다른_상품의_실패는_대기_중인_구매를_끝내지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        store.RaiseFailed("remove_ads", PurchaseFailureReason.UserCancelled);

        // 아직 끝나지 않았어야 한다 — 같은 상품의 이벤트가 와야 완료된다.
        store.RaisePending(store.Pending("gems", "tx-a"));
        Assert.AreEqual(IAPPurchaseOutcome.Purchased, (await task).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_소유한_비소모성은_스토어를_거치지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var entitlements = new FakeEntitlementStorage();
        entitlements.Owned.Add("remove_ads");
        var sut = NewService(store, entitlements: entitlements);
        await sut.InitializeAsync();

        var result = await sut.PurchaseAsync("remove_ads");

        Assert.AreEqual(IAPPurchaseOutcome.AlreadyOwned, result.Outcome);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("remove_ads", result.Purchase.ProductId);
        Assert.IsEmpty(store.PurchaseCalls, "이미 소유인데 스토어를 호출했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매가_진행_중이면_두_번째_호출은_즉시_NotReady다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();

        var first = sut.PurchaseAsync("gems");
        var second = await sut.PurchaseAsync("remove_ads");

        Assert.AreEqual(IAPPurchaseOutcome.NotReady, second.Outcome);
        Assert.AreEqual(1, store.PurchaseCalls.Count, "진행 중인데 스토어를 또 호출했다");

        store.RaisePending(store.Pending("gems", "tx-b"));
        Assert.AreEqual(IAPPurchaseOutcome.Purchased, (await first).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 카탈로그에_없는_상품_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*unknown"));

        Assert.AreEqual(IAPPurchaseOutcome.NotReady, (await sut.PurchaseAsync("unknown")).Outcome);
        Assert.IsEmpty(store.PurchaseCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트랜잭션_ID가_없는_주문은_건너뛴다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*트랜잭션"));

        // 확정할 수단이 없다 — 지급하면 다음 실행에 재전달돼 이중 지급이 된다.
        store.RaisePending(store.Pending("gems", string.Empty));
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 보류된_구매는_지급하지_않고_Deferred를_반환한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        store.RaiseDeferred("gems");
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Deferred, result.Outcome);
        Assert.IsEmpty(fulfillment.Calls);

        // 나중에 승인되면 같은 파이프라인으로 들어와 지급된다.
        var late = store.Pending("gems", "tx-late");
        store.RaisePending(late);
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count);
        CollectionAssert.AreEqual(new[] { late }, store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 미확정_구매는_초기화_때_지급되고_확정된다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var old = store.Pending("gems", "tx-old");
        store.PendingOnFetch.Add(old);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);

        await sut.InitializeAsync();
        await AwaitableTest.NextFrame();   // async void 핸들러가 끝날 틈을 준다

        Assert.AreEqual(1, fulfillment.Calls.Count, "재전달된 구매가 지급되지 않았다");
        Assert.IsTrue(fulfillment.Calls[0].IsRestored, "재전달된 구매에 복원 표시가 없다");
        CollectionAssert.AreEqual(new[] { old }, store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 카탈로그에_없는_상품의_미확정_구매는_확정하지_않고_경고한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*unknown"));

        store.RaisePending(store.Pending("unknown", "tx-x"));
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(store.ConfirmCalls, "지급할 수 없는 구매를 확정하면 영영 되찾을 수 없다");
    });
}
