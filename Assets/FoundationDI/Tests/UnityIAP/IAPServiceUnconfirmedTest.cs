using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.TestTools;

// 미확정 구매를 언제 처리하는가 — 지급 처리기 준비 대기(보류), 실행 중 재처리, 재전달과 진행 중 구매의 구분.
public class IAPServiceUnconfirmedTest
{
    private static readonly ProductDefinition[] Catalog =
    {
        UnityIAPTestObjects.Definition("gems", ProductType.Consumable),
        UnityIAPTestObjects.Definition("remove_ads", ProductType.NonConsumable),
    };

    private static IAPService NewService(UnityIAPStoreHarness store, IIAPFulfillment fulfillment)
    {
        return new IAPService(store.Store, store.Products, store.Purchases, Catalog,
                              fulfillment, new FakeReceiptValidator(), new FakeEntitlementStorage(),
                              verboseLogging: false);
    }

    // ── 지급 처리기가 준비될 때까지 보류 ─────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급_처리기가_준비되지_않았으면_재전달된_구매를_보류한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        store.PendingOnFetch.Add(store.Pending("gems", "tx-old"));
        var fulfillment = new GatedFulfillment { IsReady = false };
        var sut = NewService(store, fulfillment);

        await sut.InitializeAsync();
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(fulfillment.Calls, "준비되지 않은 지급 처리기에 지급을 맡겼다");
        Assert.IsEmpty(store.ConfirmCalls, "지급하지 않고 확정했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 준비된_뒤_ProcessUnconfirmedAsync를_부르면_보류한_구매를_지급하고_확정한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var old = store.Pending("gems", "tx-old");
        store.PendingOnFetch.Add(old);
        var fulfillment = new GatedFulfillment { IsReady = false };
        var sut = NewService(store, fulfillment);
        var published = new List<IAPPurchase>();
        sut.Purchased += p => published.Add(p);
        await sut.InitializeAsync();

        // 스토어가 같은 주문을 다시 줘도(조회) 보류 목록과 겹쳐 두 번 지급하지 않아야 한다.
        fulfillment.IsReady = true;
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count);
        Assert.IsTrue(fulfillment.Calls[0].IsRestored);
        CollectionAssert.AreEqual(new[] { old }, store.ConfirmCalls);
        Assert.AreEqual(1, published.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 보류_중에_같은_트랜잭션이_다시_와도_한_번만_지급한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        store.PendingOnFetch.Add(store.Pending("gems", "tx-old"));
        var fulfillment = new GatedFulfillment { IsReady = false };
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        store.RaisePurchasesFetched();
        store.RaisePending(store.Pending("gems", "tx-old"));

        // 스토어가 더는 주지 않아도 보류 목록만으로 처리돼야 한다.
        store.PendingOnFetch.Clear();
        fulfillment.IsReady = true;
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count);
        Assert.AreEqual(1, store.ConfirmCalls.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 준비되지_않았을_때_한_구매는_FulfillmentNotReady로_끝나고_준비되면_지급된다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new GatedFulfillment { IsReady = false };
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-new"));
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(IAPErrorCodes.FulfillmentNotReady, result.Error.Code);
        Assert.IsEmpty(store.ConfirmCalls);

        fulfillment.IsReady = true;
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, fulfillment.Calls.Count);
        Assert.IsFalse(fulfillment.Calls[0].IsRestored, "사용자가 산 구매는 보류해도 신규 구매다");
        Assert.AreEqual(1, store.ConfirmCalls.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 소모성_상품이_있는데_지급_처리기가_없으면_초기화_때_경고한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store, null);

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*지급 처리기"));

        Assert.IsTrue(await sut.InitializeAsync());
    });

    // ── 실행 중 재처리 ──────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급이_실패한_구매를_ProcessUnconfirmedAsync로_다시_처리한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment { Result = false };
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        var order = store.Pending("gems", "tx-1");
        store.RaisePending(order);
        Assert.AreEqual(IAPPurchaseOutcome.Failed, (await task).Outcome);
        Assert.IsEmpty(store.ConfirmCalls);

        // 스토어는 미확정 구매를 조회 때 다시 준다.
        store.PendingOnFetch.Add(order);
        fulfillment.Result = true;
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(2, fulfillment.Calls.Count);
        CollectionAssert.AreEqual(new[] { order }, store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator ProcessUnconfirmedAsync를_연달아_불러도_조회는_한_번만_한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store, new FakeFulfillment());
        await sut.InitializeAsync();
        store.HoldPurchasesFetch = true;

        var first = sut.ProcessUnconfirmedAsync();
        var second = sut.ProcessUnconfirmedAsync();
        store.RaisePurchasesFetched();
        await first;
        await second;

        Assert.AreEqual(2, store.Calls.Count(c => c == "FetchPurchases"), "초기화 1회 + 재처리 1회여야 한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전에는_ProcessUnconfirmedAsync가_아무것도_하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store, new FakeFulfillment());

        await sut.ProcessUnconfirmedAsync();

        Assert.IsEmpty(store.Calls.Where(c => c == "FetchPurchases"));
    });

    // ── 재전달과 진행 중 구매 ─────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매_대기_중에_같은_상품의_재전달_주문이_처리돼도_대기_중인_구매는_끝나지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");

        store.PendingOnFetch.Add(store.Pending("gems", "tx-old"));
        store.RaisePurchasesFetched();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, store.ConfirmCalls.Count, "재전달 주문은 그대로 처리된다");
        Assert.IsFalse(task.GetAwaiter().IsCompleted, "재전달 주문의 결과로 진행 중인 구매를 끝냈다");

        store.RaisePending(store.Pending("gems", "tx-new"));
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Purchased, result.Outcome);
        Assert.AreEqual("tx-new", result.Purchase.TransactionId);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 미확정_구매_때문에_거절되면_DuplicateTransaction으로_끝나고_미확정_구매를_다시_처리한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment);
        await sut.InitializeAsync();

        // 구글은 같은 소모성 상품이 미확정이면 새 구매를 ItemAlreadyOwned로 거절한다.
        var old = store.Pending("gems", "tx-old");
        store.PendingOnFetch.Add(old);

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        store.RaiseFailed("gems", PurchaseFailureReason.DuplicateTransaction);
        var result = await task;
        await AwaitableTest.NextFrame();

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(IAPErrorCodes.DuplicateTransaction, result.Error.Code);
        Assert.AreEqual(1, fulfillment.Calls.Count, "막고 있던 미확정 구매를 처리하지 않았다");
        CollectionAssert.AreEqual(new[] { old }, store.ConfirmCalls);
    });
}
