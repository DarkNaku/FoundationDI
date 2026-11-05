using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.TestTools;

// 스토어가 답하지 않거나 일부만 답할 때 — 상품 일부 누락, 연결·조회·구매 시간 제한.
public class IAPServiceTimeoutTest
{
    private static readonly ProductDefinition[] Catalog =
    {
        UnityIAPTestObjects.Definition("gems", ProductType.Consumable),
        UnityIAPTestObjects.Definition("pack", ProductType.Consumable),
        UnityIAPTestObjects.Definition("remove_ads", ProductType.NonConsumable),
    };

    private static IAPService NewService(UnityIAPStoreHarness store, ManualIAPDispatcher clock,
                                         IAPServiceOptions options = null, IIAPFulfillment fulfillment = null)
    {
        return new IAPService(store.Store, store.Products, store.Purchases, Catalog, new IAPServiceDependencies
        {
            Fulfillment = fulfillment ?? new FakeFulfillment(),
            Validator = new FakeReceiptValidator(),
            Entitlements = new FakeEntitlementStorage(),
            Options = options ?? new IAPServiceOptions(),
            Dispatcher = () => clock,
        });
    }

    // ── 상품 일부 누락 ──────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 일부_상품만_조회되면_받은_상품으로_초기화하고_빠진_ID를_경고한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        store.UnavailableProducts.Add("pack");
        var sut = NewService(store, new ManualIAPDispatcher());

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*pack"));

        Assert.IsTrue(await sut.InitializeAsync());
        CollectionAssert.AreEquivalent(new[] { "gems", "remove_ads" }, sut.Products.Select(p => p.Id).ToArray());
        Assert.IsFalse(sut.TryGetProduct("pack", out _));
        LogAssert.NoUnexpectedReceived();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 일부_누락이_먼저_와도_받은_상품을_기다려_초기화한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { ReportUnavailableFirst = true };
        store.UnavailableProducts.Add("pack");
        var sut = NewService(store, new ManualIAPDispatcher());

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*pack"));

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(2, sut.Products.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 상품이_하나도_조회되지_않으면_초기화가_실패한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        foreach (var definition in Catalog) store.UnavailableProducts.Add(definition.id);
        var sut = NewService(store, new ManualIAPDispatcher());

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*조회"));
        LogAssert.Expect(LogType.Error, new Regex("IAPService.*초기화"));

        Assert.IsFalse(await sut.InitializeAsync());
    });

    // ── 시간 제한 ───────────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 연결_이벤트가_오지_않으면_시간_제한_뒤_실패하고_다시_부르면_재시도한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { HoldConnect = true };
        var clock = new ManualIAPDispatcher();
        var sut = NewService(store, clock, new IAPServiceOptions { ConnectTimeoutSeconds = 5f });

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*연결.*5"));
        LogAssert.Expect(LogType.Error, new Regex("IAPService.*초기화"));

        var first = sut.InitializeAsync();
        clock.Advance(4.9f);
        clock.Advance(0.1f);

        Assert.IsFalse(await first);
        Assert.IsFalse(sut.IsInitialized);

        store.HoldConnect = false;

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(2, store.Calls.Count(c => c == "Connect"));
        Assert.AreEqual(0, clock.ActiveCount, "끝난 시간 제한이 남아 있다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 상품_조회가_끝나지_않으면_시간_제한_뒤_초기화가_실패한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { HoldProductsFetch = true };
        var clock = new ManualIAPDispatcher();
        var sut = NewService(store, clock, new IAPServiceOptions { FetchTimeoutSeconds = 10f });

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*상품 조회.*10"));
        LogAssert.Expect(LogType.Error, new Regex("IAPService.*초기화"));

        var init = sut.InitializeAsync();
        clock.Advance(10f);

        Assert.IsFalse(await init);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매_조회가_끝나지_않아도_시간_제한_뒤_초기화는_성공한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog) { HoldPurchasesFetch = true };
        var clock = new ManualIAPDispatcher();
        var sut = NewService(store, clock, new IAPServiceOptions { FetchTimeoutSeconds = 10f });

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*구매 조회.*10"));

        var init = sut.InitializeAsync();
        clock.Advance(10f);

        Assert.IsTrue(await init);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매_결과가_오지_않으면_시간_제한_뒤_Timeout으로_끝나고_늦게_온_주문은_지급된다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var clock = new ManualIAPDispatcher();
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, clock, new IAPServiceOptions { PurchaseTimeoutSeconds = 10f }, fulfillment);
        await sut.InitializeAsync();
        var published = 0;
        sut.Purchased += _ => published++;

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*구매.*10"));

        var task = sut.PurchaseAsync("gems");
        clock.Advance(10f);
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(IAPErrorCodes.Timeout, result.Error.Code);

        // 결제는 스토어에서 끝났을 수 있다 — 늦게 온 주문도 지급·확정하고 Purchased를 낸다.
        store.RaisePending(store.Pending("gems", "tx-late"));
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count);
        Assert.AreEqual(1, store.ConfirmCalls.Count);
        Assert.AreEqual(1, published);

        // 다음 구매를 막지 않는다.
        var next = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-next"));
        Assert.AreEqual(IAPPurchaseOutcome.Purchased, (await next).Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매_시간_제한은_기본으로_꺼져_있다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var clock = new ManualIAPDispatcher();
        var sut = NewService(store, clock);
        await sut.InitializeAsync();

        // 스토어 시트에서 결제 수단을 고르는 동안은 얼마든지 길 수 있다.
        var task = sut.PurchaseAsync("gems");
        clock.Advance(3600f);

        Assert.IsFalse(task.GetAwaiter().IsCompleted);

        store.RaisePending(store.Pending("gems", "tx-1"));
        Assert.AreEqual(IAPPurchaseOutcome.Purchased, (await task).Outcome);
    });
}
