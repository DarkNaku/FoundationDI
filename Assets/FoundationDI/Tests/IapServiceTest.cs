using System.Collections;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class IapServiceTest
{
    private static readonly IapProductDefinition[] Catalog =
    {
        new("gems", "gems_store", IapProductType.Consumable),
        new("remove_ads", "remove_ads_store", IapProductType.NonConsumable),
    };

    private static IapService NewService(FakeIapProvider provider,
                                         FakeFulfillment fulfillment = null,
                                         FakeReceiptValidator validator = null,
                                         FakeEntitlementStorage entitlements = null)
    {
        return new IapService(provider, new IapServiceOptions(Catalog, false),
                              fulfillment ?? new FakeFulfillment(),
                              validator ?? new FakeReceiptValidator(),
                              entitlements ?? new FakeEntitlementStorage());
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화하면_provider_상품이_노출된다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);

        var ok = await sut.InitializeAsync();

        Assert.IsTrue(ok);
        Assert.IsTrue(sut.IsInitialized);
        Assert.AreEqual(2, sut.Products.Count);
        Assert.IsTrue(sut.TryGetProduct("gems", out var gems));
        Assert.AreEqual("gems_store", gems.StoreId);
        Assert.IsFalse(sut.TryGetProduct("unknown", out _));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_실패하면_상품이_비고_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider { InitializeResult = false };
        var sut = NewService(provider);

        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var ok = await sut.InitializeAsync();

        Assert.IsFalse(ok);
        Assert.IsFalse(sut.IsInitialized);
        Assert.IsEmpty(sut.Products);
        Assert.AreEqual(IapPurchaseOutcome.NotReady, (await sut.PurchaseAsync("gems")).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService(new FakeIapProvider());

        var result = await sut.PurchaseAsync("gems");

        Assert.AreEqual(IapPurchaseOutcome.NotReady, result.Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator InitializeAsync는_재진입해도_provider를_한_번만_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);

        var first = sut.InitializeAsync();
        var second = sut.InitializeAsync();

        Assert.IsTrue(await first);
        Assert.IsTrue(await second);
        Assert.AreEqual(1, provider.InitializeCount);

        // 이미 끝난 뒤의 호출도 provider를 다시 건드리지 않는다.
        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, provider.InitializeCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매가_검증_지급_확정_순서로_진행된다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var fulfillment = new FakeFulfillment();
        var validator = new FakeReceiptValidator();
        var sut = NewService(provider, fulfillment, validator);
        await sut.InitializeAsync();

        var publishedCount = 0;
        var publishedId = (string)null;
        sut.Purchased += p => { publishedCount++; publishedId = p.ProductId; };

        var task = sut.PurchaseAsync("gems");
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-1", "receipt-1", false));
        var result = await task;

        Assert.AreEqual(IapPurchaseOutcome.Purchased, result.Outcome);
        Assert.AreEqual("gems", result.Purchase.ProductId);
        Assert.AreEqual("receipt-1", result.Purchase.Receipt);
        Assert.AreEqual(0.99, result.Purchase.Price, 0.0001);
        Assert.AreEqual("USD", result.Purchase.CurrencyCode);

        CollectionAssert.AreEqual(new[] { "gems_store" }, provider.PurchaseCalls);
        Assert.AreEqual(1, validator.CallCount);
        Assert.AreEqual(1, fulfillment.Calls.Count);
        Assert.AreEqual("gems", fulfillment.Calls[0].ProductId);
        CollectionAssert.AreEqual(new[] { "tx-1" }, provider.ConfirmCalls);
        Assert.AreEqual(1, publishedCount, "확정 후 Purchased 이벤트가 발행되지 않았다");
        Assert.AreEqual("gems", publishedId);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급이_실패하면_확정하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var fulfillment = new FakeFulfillment { Result = false };
        var sut = NewService(provider, fulfillment);
        await sut.InitializeAsync();

        var published = 0;
        sut.Purchased += _ => published++;

        var task = sut.PurchaseAsync("remove_ads");
        provider.RaisePending(new IapPendingPurchase("remove_ads_store", "tx-2", "r", false));
        var result = await task;

        Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
        Assert.IsEmpty(provider.ConfirmCalls, "지급에 실패했는데 확정했다");
        Assert.IsFalse(sut.IsOwned("remove_ads"), "지급에 실패했는데 소유로 기록했다");
        Assert.AreEqual(0, published);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급이_예외를_던져도_확정하지_않고_서비스가_살아있다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider, new FakeFulfillment { Throw = true });
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-3", "r", false));
        var result = await task;

        Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
        Assert.IsEmpty(provider.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 영수증_검증에_실패하면_지급도_확정도_하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var fulfillment = new FakeFulfillment();
        var sut = NewService(provider, fulfillment, new FakeReceiptValidator { Result = false });
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-4", "bad", false));
        var result = await task;

        Assert.AreEqual(IapPurchaseOutcome.InvalidReceipt, result.Outcome);
        Assert.AreEqual(-1, result.Error.Code);
        Assert.IsEmpty(fulfillment.Calls, "검증에 실패했는데 지급했다");
        Assert.IsEmpty(provider.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비소모성은_확정_후_소유로_기록되고_소모성은_아니다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);
        await sut.InitializeAsync();

        string ownedChanged = null;
        sut.OwnedChanged += id => ownedChanged = id;

        var nonConsumable = sut.PurchaseAsync("remove_ads");
        provider.RaisePending(new IapPendingPurchase("remove_ads_store", "tx-5", "r", false));
        await nonConsumable;

        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.AreEqual("remove_ads", ownedChanged);

        var consumable = sut.PurchaseAsync("gems");
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-6", "r", false));
        await consumable;

        Assert.IsFalse(sut.IsOwned("gems"), "소모성이 소유로 기록됐다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 카탈로그에_없는_구매가_도착하면_확정하지_않고_경고한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var fulfillment = new FakeFulfillment();
        var sut = NewService(provider, fulfillment);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        provider.RaisePending(new IapPendingPurchase("unknown_store", "tx-x", "r", false));
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(provider.ConfirmCalls, "지급할 수 없는 구매를 확정하면 영영 되찾을 수 없다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 사용자_취소와_그_외_실패를_구분한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);
        await sut.InitializeAsync();

        var cancelling = sut.PurchaseAsync("gems");
        provider.RaiseFailed(new IapPurchaseFailure("gems_store", true, new IapError(0, "cancelled")));
        Assert.AreEqual(IapPurchaseOutcome.UserCancelled, (await cancelling).Outcome);

        var failing = sut.PurchaseAsync("gems");
        provider.RaiseFailed(new IapPurchaseFailure("gems_store", false, new IapError(7, "network")));
        var result = await failing;

        Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(7, result.Error.Code);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 다른_상품의_실패는_대기_중인_구매를_끝내지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        provider.RaiseFailed(new IapPurchaseFailure("remove_ads_store", true, new IapError(0, "cancelled")));

        // 아직 끝나지 않았어야 한다 — 같은 상품의 이벤트가 와야 완료된다.
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-a", "r", false));
        Assert.AreEqual(IapPurchaseOutcome.Purchased, (await task).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_소유한_비소모성은_스토어를_거치지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var entitlements = new FakeEntitlementStorage();
        entitlements.Owned.Add("remove_ads");
        var sut = NewService(provider, entitlements: entitlements);
        await sut.InitializeAsync();

        var result = await sut.PurchaseAsync("remove_ads");

        Assert.AreEqual(IapPurchaseOutcome.AlreadyOwned, result.Outcome);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("remove_ads", result.Purchase.ProductId);
        Assert.IsEmpty(provider.PurchaseCalls, "이미 소유인데 스토어를 호출했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매가_진행_중이면_두_번째_호출은_즉시_NotReady다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);
        await sut.InitializeAsync();

        var first = sut.PurchaseAsync("gems");
        var second = await sut.PurchaseAsync("remove_ads");

        Assert.AreEqual(IapPurchaseOutcome.NotReady, second.Outcome);
        Assert.AreEqual(1, provider.PurchaseCalls.Count, "진행 중인데 스토어를 또 호출했다");

        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-7", "r", false));
        Assert.AreEqual(IapPurchaseOutcome.Purchased, (await first).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator provider가_구매_시작을_거부하면_Failed다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider { PurchaseResult = false };
        var sut = NewService(provider);
        await sut.InitializeAsync();

        var result = await sut.PurchaseAsync("gems");
        Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);

        // 대기가 비워졌어야 다음 구매를 시작할 수 있다.
        provider.PurchaseResult = true;
        var retry = sut.PurchaseAsync("gems");
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-8", "r", false));
        Assert.AreEqual(IapPurchaseOutcome.Purchased, (await retry).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 카탈로그에_없는_상품_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService(new FakeIapProvider());
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        Assert.AreEqual(IapPurchaseOutcome.NotReady, (await sut.PurchaseAsync("unknown")).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 미확정_구매는_초기화_때_지급되고_확정된다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        provider.PendingOnInitialize.Add(new IapPendingPurchase("gems_store", "tx-old", "r", false));
        var fulfillment = new FakeFulfillment();
        var sut = NewService(provider, fulfillment);

        await sut.InitializeAsync();
        await AwaitableTest.NextFrame();   // async void 핸들러가 끝날 틈을 준다

        Assert.AreEqual(1, fulfillment.Calls.Count, "재전달된 구매가 지급되지 않았다");
        CollectionAssert.AreEqual(new[] { "tx-old" }, provider.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원은_비소모성_소유를_되살리고_개수를_보고한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        provider.PendingOnRestore.Add(new IapPendingPurchase("remove_ads_store", "tx-r", "r", true));
        var sut = NewService(provider);
        await sut.InitializeAsync();

        string ownedChanged = null;
        sut.OwnedChanged += id => ownedChanged = id;

        var result = await sut.RestoreAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.RestoredCount);
        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.AreEqual("remove_ads", ownedChanged);
        CollectionAssert.AreEqual(new[] { "tx-r" }, provider.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원이_실패하면_Success가_거짓이다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider { RestoreResult = false };
        var sut = NewService(provider);
        await sut.InitializeAsync();

        var result = await sut.RestoreAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.RestoredCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전_복원은_실패한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService(new FakeIapProvider());

        var result = await sut.RestoreAsync();

        Assert.IsFalse(result.Success);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 보류된_구매는_지급하지_않고_Deferred를_반환한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var fulfillment = new FakeFulfillment();
        var sut = NewService(provider, fulfillment);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        provider.RaiseDeferred("gems_store");
        var result = await task;

        Assert.AreEqual(IapPurchaseOutcome.Deferred, result.Outcome);
        Assert.IsEmpty(fulfillment.Calls);

        // 나중에 승인되면 같은 파이프라인으로 들어와 지급된다.
        provider.RaisePending(new IapPendingPurchase("gems_store", "tx-late", "r", false));
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count);
        CollectionAssert.AreEqual(new[] { "tx-late" }, provider.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_provider가_해제되고_이후_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);
        await sut.InitializeAsync();

        sut.Dispose();
        sut.Dispose();   // 중복 Dispose도 안전해야 한다

        Assert.IsTrue(provider.IsDisposed);
        Assert.AreEqual(IapPurchaseOutcome.NotReady, (await sut.PurchaseAsync("gems")).Outcome);
        Assert.IsFalse((await sut.RestoreAsync()).Success);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_대기_중인_구매가_매달리지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeIapProvider();
        var sut = NewService(provider);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        sut.Dispose();

        Assert.AreEqual(IapPurchaseOutcome.Failed, (await task).Outcome);
    });
}
