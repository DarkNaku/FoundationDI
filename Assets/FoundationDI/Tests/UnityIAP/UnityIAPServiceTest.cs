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
        store.ConfirmedOnFetch.Add(store.Confirmed("remove_ads"));   // 스토어도 소유로 본다
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

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정_주문의_비소모성이_소유로_기록되고_OwnedChanged가_발행된다() => AwaitableTest.Run(async () =>
    {
        // 재설치 후: 로컬 캐시는 비었지만 스토어는 이미 확정된 구매를 안다.
        var store = new UnityIAPStoreHarness(Catalog);
        store.ConfirmedOnFetch.Add(store.Confirmed("remove_ads"));
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);

        string ownedChanged = null;
        sut.OwnedChanged += id => ownedChanged = id;

        await sut.InitializeAsync();

        Assert.IsTrue(sut.IsOwned("remove_ads"), "스토어가 확인한 소유가 기록되지 않았다");
        Assert.AreEqual("remove_ads", ownedChanged);
        Assert.IsEmpty(fulfillment.Calls, "이미 확정된 구매를 다시 지급했다");
        Assert.IsEmpty(store.ConfirmCalls, "이미 확정된 구매를 다시 확정했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 조회_결과에_없는_비소모성_소유는_회수된다() => AwaitableTest.Run(async () =>
    {
        // 환불: 캐시에는 남아 있지만 스토어는 더 이상 소유로 보지 않는다.
        var store = new UnityIAPStoreHarness(Catalog);
        var entitlements = new FakeEntitlementStorage();
        entitlements.Owned.Add("remove_ads");
        var sut = NewService(store, entitlements: entitlements);

        string ownedChanged = null;
        sut.OwnedChanged += id => ownedChanged = id;

        await sut.InitializeAsync();

        Assert.IsFalse(sut.IsOwned("remove_ads"), "환불된 소유가 회수되지 않았다");
        Assert.AreEqual("remove_ads", ownedChanged);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 소유_상태가_그대로면_OwnedChanged를_발행하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        store.ConfirmedOnFetch.Add(store.Confirmed("remove_ads"));
        var entitlements = new FakeEntitlementStorage();
        entitlements.Owned.Add("remove_ads");
        var sut = NewService(store, entitlements: entitlements);

        var changed = 0;
        sut.OwnedChanged += _ => changed++;

        // 조회는 실행마다 온다. 매번 이벤트가 나가면 구독한 UI가 이유 없이 갱신된다.
        await sut.InitializeAsync();

        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.AreEqual(0, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 소모성과_카탈로그에_없는_상품은_소유_목록에서_무시한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        store.ConfirmedOnFetch.Add(store.Confirmed("gems"));
        store.ConfirmedOnFetch.Add(store.Confirmed("unknown"));
        var sut = NewService(store);

        var changed = 0;
        sut.OwnedChanged += _ => changed++;

        await sut.InitializeAsync();

        Assert.IsFalse(sut.IsOwned("gems"), "소모성이 소유로 기록됐다");
        Assert.IsFalse(sut.IsOwned("unknown"));
        Assert.AreEqual(0, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급_중인_주문이_다음_조회에_또_와도_두_번_지급하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new HeldFulfillment();
        var sut = new UnityIAPService(store.Store, store.Products, store.Purchases, Catalog, fulfillment,
                                      new FakeReceiptValidator(), new FakeEntitlementStorage(), false);
        await sut.InitializeAsync();

        var order = store.Pending("gems", "tx-1");
        store.RaisePending(order);

        // 지급이 끝나기 전에 조회가 같은 주문을 미확정으로 다시 실어 온다(복원 등).
        store.PendingOnFetch.Add(order);
        store.RaisePurchasesFetched();

        fulfillment.ReleaseAll();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count, "지급 중인 주문을 또 지급했다");
        Assert.AreEqual(1, store.ConfirmCalls.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이번_세션에_확정한_주문이_조회에_미확정으로_와도_다시_지급하지_않고_소유로_센다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        var order = store.Pending("remove_ads", "tx-1");
        var task = sut.PurchaseAsync("remove_ads");
        store.RaisePending(order);
        await task;

        var changed = 0;
        sut.OwnedChanged += _ => changed++;

        // 확정은 스토어에 비동기로 반영된다 — 직후의 조회가 같은 주문을 아직 미확정으로 돌려줄 수 있다.
        store.PendingOnFetch.Add(order);
        store.RaisePurchasesFetched();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count, "확정한 주문을 다시 지급했다");
        Assert.IsTrue(sut.IsOwned("remove_ads"), "확정이 반영되기 전의 조회 결과로 소유를 회수했다");
        Assert.AreEqual(0, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원은_콜백과_뒤따르는_조회가_모두_도착해야_끝난다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();

        // 실제 SDK처럼 콜백이 먼저 오고, 복원된 구매를 실은 조회 결과는 나중에 온다.
        store.HoldRestoreFetch = true;
        store.ConfirmedOnFetch.Add(store.Confirmed("remove_ads"));

        var done = false;
        IAPRestoreResult result = default;

        async Awaitable Restore()
        {
            result = await sut.RestoreAsync();
            done = true;
        }

        _ = Restore();
        await AwaitableTest.NextFrame();

        Assert.IsFalse(done, "조회 결과가 오기 전에 복원이 끝났다");

        store.RaisePurchasesFetched();
        await AwaitableTest.WaitUntil(() => done, 1f);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.RestoredCount);
        Assert.IsTrue(sut.IsOwned("remove_ads"));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원은_스토어가_확인한_비소모성_개수를_보고한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        // 조회가 콜백보다 먼저 오는 순서(하네스 기본값).
        store.ConfirmedOnFetch.Add(store.Confirmed("remove_ads"));

        var result = await sut.RestoreAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.RestoredCount);
        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.IsEmpty(fulfillment.Calls, "확정된 구매를 복원하면서 다시 지급했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원은_미확정_구매를_지급하고_개수에_포함한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        var pending = store.Pending("remove_ads", "tx-r");
        store.PendingOnFetch.Add(pending);

        var result = await sut.RestoreAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.RestoredCount);
        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.IsTrue(fulfillment.Calls[0].IsRestored);
        CollectionAssert.AreEqual(new[] { pending }, store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원이_실패하면_Success가_거짓이다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();
        store.RestoreSucceeds = false;

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        var result = await sut.RestoreAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.RestoredCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원_뒤의_조회가_실패하면_Success가_거짓이다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);
        await sut.InitializeAsync();
        store.PurchasesFetchSucceeds = false;

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        Assert.IsFalse((await sut.RestoreAsync()).Success);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전_복원은_실패한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store);

        Assert.IsFalse((await sut.RestoreAsync()).Success);
        CollectionAssert.DoesNotContain(store.Calls, "Restore");
    });
}
