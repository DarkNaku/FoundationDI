using System.Collections;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.TestTools;

// 서버 영수증 검증 훅과 검증 실패 구매의 처리 방식.
public class IAPServiceValidationTest
{
    private static readonly ProductDefinition[] Catalog =
    {
        UnityIAPTestObjects.Definition("gems", ProductType.Consumable),
        UnityIAPTestObjects.Definition("remove_ads", ProductType.NonConsumable),
    };

    private static IAPService NewService(UnityIAPStoreHarness store, FakeFulfillment fulfillment,
                                         FakeReceiptValidator validator = null,
                                         FakeAsyncReceiptValidator asyncValidator = null,
                                         IAPServiceOptions options = null)
    {
        return new IAPService(store.Store, store.Products, store.Purchases, Catalog, new IAPServiceDependencies
        {
            Fulfillment = fulfillment,
            Validator = validator ?? new FakeReceiptValidator(),
            AsyncValidator = asyncValidator,
            Entitlements = new FakeEntitlementStorage(),
            Options = options ?? new IAPServiceOptions(),
        });
    }

    // ── 비동기(서버) 검증 ────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비동기_검증기가_Valid면_지급하고_확정한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var asyncValidator = new FakeAsyncReceiptValidator();
        var sut = NewService(store, fulfillment, asyncValidator: asyncValidator);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-1", "receipt-1"));

        Assert.AreEqual(IAPPurchaseOutcome.Purchased, (await task).Outcome);
        Assert.AreEqual(1, asyncValidator.Calls.Count);
        Assert.AreEqual("receipt-1", asyncValidator.Calls[0].Receipt);
        Assert.AreEqual(1, fulfillment.Calls.Count);
        Assert.AreEqual(1, store.ConfirmCalls.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비동기_검증기가_Invalid면_지급도_확정도_하지_않고_InvalidReceipt로_끝난다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var asyncValidator = new FakeAsyncReceiptValidator { Result = IAPValidationResult.Invalid("서버가 거절") };
        var sut = NewService(store, fulfillment, asyncValidator: asyncValidator);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*검증.*서버가 거절"));

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-1"));
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.InvalidReceipt, result.Outcome);
        Assert.AreEqual(IAPErrorCodes.ReceiptRejected, result.Error.Code);
        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비동기_검증기가_RetryLater면_확정하지_않고_재처리_때_다시_검증한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var asyncValidator = new FakeAsyncReceiptValidator { Result = IAPValidationResult.RetryLater("서버 점검") };
        var sut = NewService(store, fulfillment, asyncValidator: asyncValidator);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*서버 점검"));

        var task = sut.PurchaseAsync("gems");
        var order = store.Pending("gems", "tx-1");
        store.RaisePending(order);
        var result = await task;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(IAPErrorCodes.ValidationRetryLater, result.Error.Code);
        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(store.ConfirmCalls);

        store.PendingOnFetch.Add(order);
        asyncValidator.Result = IAPValidationResult.Valid();
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(2, asyncValidator.Calls.Count);
        Assert.AreEqual(1, fulfillment.Calls.Count);
        CollectionAssert.AreEqual(new[] { order }, store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비동기_검증기가_예외를_던지면_RetryLater로_본다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var sut = NewService(store, fulfillment, asyncValidator: new FakeAsyncReceiptValidator { Throw = true });
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*예외"));
        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-1"));
        var result = await task;

        Assert.AreEqual(IAPErrorCodes.ValidationRetryLater, result.Error.Code);
        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(store.ConfirmCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 로컬_검증이_실패하면_비동기_검증기를_부르지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var asyncValidator = new FakeAsyncReceiptValidator();
        var sut = NewService(store, new FakeFulfillment(), new FakeReceiptValidator { Result = false }, asyncValidator);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-1"));

        Assert.AreEqual(IAPPurchaseOutcome.InvalidReceipt, (await task).Outcome);
        Assert.IsEmpty(asyncValidator.Calls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비동기_검증을_기다리는_사이_해제되면_확정하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var asyncValidator = new FakeAsyncReceiptValidator { Hold = true };
        var sut = NewService(store, fulfillment, asyncValidator: asyncValidator);
        await sut.InitializeAsync();

        var task = sut.PurchaseAsync("gems");
        store.RaisePending(store.Pending("gems", "tx-1"));

        sut.Dispose();
        asyncValidator.Release(IAPValidationResult.Valid());
        await AwaitableTest.NextFrame();

        Assert.AreEqual(IAPErrorCodes.Disposed, (await task).Error.Code);
        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(store.ConfirmCalls);
    });

    // ── 검증 실패 구매 처리 방식 ──────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 기본_정책은_검증_실패_구매를_확정하지_않고_로그는_트랜잭션마다_한_번만_남긴다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var sut = NewService(store, new FakeFulfillment(), new FakeReceiptValidator { Result = false });
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*검증"));

        store.RaisePending(store.Pending("gems", "tx-forged"));
        store.RaisePending(store.Pending("gems", "tx-forged"));
        store.PendingOnFetch.Add(store.Pending("gems", "tx-forged"));
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(store.ConfirmCalls);
        LogAssert.NoUnexpectedReceived();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator ConfirmWithoutGrant면_검증_실패한_소모성_구매를_지급_없이_확정한다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var fulfillment = new FakeFulfillment();
        var options = new IAPServiceOptions { InvalidReceiptPolicy = InvalidReceiptPolicy.ConfirmWithoutGrant };
        var sut = NewService(store, fulfillment, new FakeReceiptValidator { Result = false }, options: options);
        await sut.InitializeAsync();
        var published = 0;
        sut.Purchased += _ => published++;

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*검증"));

        var task = sut.PurchaseAsync("gems");
        var order = store.Pending("gems", "tx-forged");
        store.RaisePending(order);

        Assert.AreEqual(IAPPurchaseOutcome.InvalidReceipt, (await task).Outcome);
        Assert.IsEmpty(fulfillment.Calls);
        Assert.AreEqual(0, published);
        CollectionAssert.AreEqual(new[] { order }, store.ConfirmCalls, "버리지 않으면 매 실행 재전달된다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator ConfirmWithoutGrant여도_비소모성은_확정하지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnityIAPStoreHarness(Catalog);
        var options = new IAPServiceOptions { InvalidReceiptPolicy = InvalidReceiptPolicy.ConfirmWithoutGrant };
        var sut = NewService(store, new FakeFulfillment(), new FakeReceiptValidator { Result = false }, options: options);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*검증"));

        // 확정하면 스토어의 확정 목록에 올라 소유 동기화가 지급 없이 소유로 기록한다.
        store.RaisePending(store.Pending("remove_ads", "tx-forged"));
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(store.ConfirmCalls);
        Assert.IsFalse(sut.IsOwned("remove_ads"));
    });
}
