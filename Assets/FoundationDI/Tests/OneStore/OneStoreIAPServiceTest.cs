using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.TestTools;

// 원스토어 정책 계층 — 소모성은 소비, 비소모성은 승인으로 확정하고, 승인된 비소모성을 소유 목록으로 본다.
public class OneStoreIAPServiceTest
{
    private static readonly ProductDefinition[] Catalog =
    {
        new("gems", "gems_os", ProductType.Consumable),
        new("remove_ads", "remove_ads_os", ProductType.NonConsumable),
    };

    private FakeOneStoreClient _client;
    private FakeFulfillment _fulfillment;
    private FakeReceiptValidator _validator;
    private FakeEntitlementStorage _entitlements;

    [SetUp]
    public void SetUp()
    {
        _client = new FakeOneStoreClient();
        _client.Products.Add(FakeOneStoreClient.Product("gems_os", "1200"));
        _client.Products.Add(FakeOneStoreClient.Product("remove_ads_os", "3300"));
        _fulfillment = new FakeFulfillment();
        _validator = new FakeReceiptValidator();
        _entitlements = new FakeEntitlementStorage();
    }

    private OneStoreIAPService NewService(IIAPFulfillment fulfillment = null, IAPServiceOptions options = null,
                                          ManualIAPDispatcher clock = null)
    {
        return new OneStoreIAPService(_client, Catalog, new IAPServiceDependencies
        {
            Fulfillment = fulfillment ?? _fulfillment,
            Validator = _validator,
            Entitlements = _entitlements,
            Options = options ?? new IAPServiceOptions(),
            Dispatcher = clock == null ? null : () => clock,
        });
    }

    // ── 초기화 ────────────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 상품을_조회해_공용_ID와_원화_가격으로_초기화한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();

        Assert.IsTrue(await sut.InitializeAsync());

        Assert.IsTrue(sut.TryGetProduct("gems", out var gems));
        Assert.AreEqual("gems_os", gems.StoreId);
        Assert.AreEqual(IAPProductType.Consumable, gems.Type);
        Assert.AreEqual("₩1,200", gems.LocalizedPrice);
        Assert.AreEqual(1200.0, gems.Price);
        Assert.AreEqual("KRW", gems.CurrencyCode);
        Assert.IsTrue(gems.IsAvailable);
        Assert.AreEqual(2, sut.Products.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 일부_상품만_조회되면_받은_상품으로_초기화하고_빠진_ID를_경고한다() => AwaitableTest.Run(async () =>
    {
        _client.Products.RemoveAll(p => p.ProductId == "remove_ads_os");
        var sut = NewService();

        LogAssert.Expect(LogType.Warning, new Regex("OneStoreIAPService.*remove_ads"));
        Assert.IsTrue(await sut.InitializeAsync());

        Assert.AreEqual(1, sut.Products.Count);
        Assert.IsFalse(sut.TryGetProduct("remove_ads", out _));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 상품_조회가_실패하면_초기화가_실패한다() => AwaitableTest.Run(async () =>
    {
        _client.ProductsResult = new OneStoreResult(OneStoreResponseCodes.NeedLogin, "login");
        var sut = NewService();

        LogAssert.Expect(LogType.Error, new Regex("OneStoreIAPService.*상품 조회"));
        LogAssert.Expect(LogType.Error, new Regex("OneStoreIAPService.*초기화"));
        Assert.IsFalse(await sut.InitializeAsync());
        Assert.IsFalse(sut.IsInitialized);
        Assert.AreEqual(0, _client.QueryPurchasesCount, "상품 조회가 실패했는데 구매를 조회했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 연결과_상품_조회가_끝나지_않으면_두_시간_제한을_더한_뒤_실패한다() => AwaitableTest.Run(async () =>
    {
        _client.HoldProducts = true;
        var clock = new ManualIAPDispatcher();
        var sut = NewService(options: new IAPServiceOptions { ConnectTimeoutSeconds = 10f, FetchTimeoutSeconds = 5f },
                             clock: clock);

        var initializing = sut.InitializeAsync();

        clock.Advance(14f);
        Assert.IsFalse(sut.IsInitialized);

        LogAssert.Expect(LogType.Warning, new Regex("OneStoreIAPService.*15초"));
        LogAssert.Expect(LogType.Error, new Regex("OneStoreIAPService.*초기화"));
        clock.Advance(1f);

        Assert.IsFalse(await initializing);
    });

    // ── 구매 ──────────────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 소모성을_사면_지급하고_소비한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        var published = new List<IAPPurchase>();
        sut.Purchased += p => published.Add(p);
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("gems");
        CollectionAssert.AreEqual(new[] { "gems_os" }, _client.PurchaseCalls, "스토어 ID로 사지 않았다");

        _client.CompletePurchase(FakeOneStoreClient.Purchase("gems_os", "order-1"));
        var result = await buying;

        Assert.AreEqual(IAPPurchaseOutcome.Purchased, result.Outcome);
        Assert.AreEqual("gems", result.Purchase.ProductId);
        Assert.AreEqual("order-1", result.Purchase.TransactionId);
        Assert.AreEqual("receipt-order-1", result.Purchase.Receipt);
        Assert.AreEqual(1200.0, result.Purchase.Price);
        Assert.AreEqual(1, _fulfillment.Calls.Count);
        Assert.AreEqual(1, _client.Consumed.Count);
        Assert.IsEmpty(_client.Acknowledged, "소모성을 승인했다 — 소비하지 않으면 다시 살 수 없다");
        Assert.AreEqual(1, published.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 비소모성을_사면_지급하고_승인하며_소유로_기록한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        var changed = new List<string>();
        sut.OwnedChanged += changed.Add;
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("remove_ads");
        _client.CompletePurchase(FakeOneStoreClient.Purchase("remove_ads_os", "order-2"));
        var result = await buying;

        Assert.AreEqual(IAPPurchaseOutcome.Purchased, result.Outcome);
        Assert.AreEqual(1, _client.Acknowledged.Count);
        Assert.IsEmpty(_client.Consumed, "비소모성을 소비했다 — 소유가 사라진다");
        Assert.IsTrue(sut.IsOwned("remove_ads"));
        CollectionAssert.AreEqual(new[] { "remove_ads" }, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_소유한_비소모성은_결제_화면을_띄우지_않는다() => AwaitableTest.Run(async () =>
    {
        _entitlements.Owned.Add("remove_ads");
        _client.Stored.Add(FakeOneStoreClient.Purchase("remove_ads_os", "order-old", acknowledged: true));
        var sut = NewService();
        await sut.InitializeAsync();

        var result = await sut.PurchaseAsync("remove_ads");

        Assert.AreEqual(IAPPurchaseOutcome.AlreadyOwned, result.Outcome);
        Assert.IsEmpty(_client.PurchaseCalls);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 사용자가_취소하면_UserCancelled로_끝나고_로그를_남기지_않는다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("gems");
        _client.FailPurchase(OneStoreResponseCodes.UserCanceled);

        Assert.AreEqual(IAPPurchaseOutcome.UserCancelled, (await buying).Outcome);
        LogAssert.NoUnexpectedReceived();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 그_밖의_실패는_원스토어_응답_코드로_끝난다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("gems");
        LogAssert.Expect(LogType.Warning, new Regex("OneStoreIAPService.*gems"));
        _client.FailPurchase(3, "billing unavailable");
        var result = await buying;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(3, result.Error.Code);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 진행_중인_구매가_있으면_두_번째_구매는_NotReady다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        var first = sut.PurchaseAsync("gems");
        var second = await sut.PurchaseAsync("gems");

        Assert.AreEqual(IAPPurchaseOutcome.NotReady, second.Outcome);
        Assert.AreEqual(1, _client.PurchaseCalls.Count);

        _client.CompletePurchase(FakeOneStoreClient.Purchase("gems_os", "order-1"));
        Assert.IsTrue((await first).IsSuccess);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급이_실패하면_확정하지_않는다() => AwaitableTest.Run(async () =>
    {
        _fulfillment.Result = false;
        var sut = NewService();
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("gems");
        _client.CompletePurchase(FakeOneStoreClient.Purchase("gems_os", "order-1"));
        var result = await buying;

        Assert.AreEqual(IAPPurchaseOutcome.Failed, result.Outcome);
        Assert.AreEqual(IAPErrorCodes.FulfillmentFailed, result.Error.Code);
        Assert.IsEmpty(_client.Consumed, "지급하지 않고 소비했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 영수증_검증이_실패하면_지급도_확정도_하지_않는다() => AwaitableTest.Run(async () =>
    {
        _validator.Result = false;
        var sut = NewService();
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("gems");
        LogAssert.Expect(LogType.Error, new Regex("OneStoreIAPService.*검증"));
        _client.CompletePurchase(FakeOneStoreClient.Purchase("gems_os", "order-1"));
        var result = await buying;

        Assert.AreEqual(IAPPurchaseOutcome.InvalidReceipt, result.Outcome);
        Assert.IsEmpty(_fulfillment.Calls);
        Assert.IsEmpty(_client.Consumed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_소유_응답이면_DuplicateTransaction으로_끝나고_미확정_구매를_지급한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        // 지난 실행에서 결제만 되고 소비하지 못한 구매가 남아 같은 상품의 새 구매를 막는다.
        _client.Stored.Add(FakeOneStoreClient.Purchase("gems_os", "order-old"));

        var buying = sut.PurchaseAsync("gems");
        LogAssert.Expect(LogType.Warning, new Regex("OneStoreIAPService.*gems"));
        _client.FailPurchase(OneStoreResponseCodes.ItemAlreadyOwned);
        var result = await buying;
        await AwaitableTest.NextFrame();

        Assert.AreEqual(IAPErrorCodes.DuplicateTransaction, result.Error.Code);
        Assert.AreEqual(1, _fulfillment.Calls.Count, "막고 있던 구매를 지급하지 않았다");
        Assert.IsTrue(_fulfillment.Calls[0].IsRestored);
        Assert.AreEqual(1, _client.Consumed.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 결제_완료_상태가_아닌_구매는_지급하지_않는다() => AwaitableTest.Run(async () =>
    {
        _client.Stored.Add(FakeOneStoreClient.Purchase("gems_os", "order-refunded", state: 2));
        var sut = NewService();

        await sut.InitializeAsync();
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(_fulfillment.Calls);
        Assert.IsEmpty(_client.Consumed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 요청_없이_온_구매도_지급하고_확정한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        _client.RaisePurchasesUpdated(FakeOneStoreClient.Purchase("gems_os", "order-promo"));
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, _fulfillment.Calls.Count);
        Assert.AreEqual(1, _client.Consumed.Count);
    });

    // ── 미확정 구매·소유 동기화 ────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_때_미확정_구매를_재전달로_지급하고_확정한다() => AwaitableTest.Run(async () =>
    {
        _client.Stored.Add(FakeOneStoreClient.Purchase("gems_os", "order-old"));
        _client.Stored.Add(FakeOneStoreClient.Purchase("remove_ads_os", "order-unacked"));
        var sut = NewService();

        Assert.IsTrue(await sut.InitializeAsync());
        await AwaitableTest.NextFrame();

        Assert.AreEqual(2, _fulfillment.Calls.Count);
        Assert.IsTrue(_fulfillment.Calls.TrueForAll(p => p.IsRestored));
        Assert.AreEqual(1, _client.Consumed.Count);
        Assert.AreEqual(1, _client.Acknowledged.Count);
        Assert.IsTrue(sut.IsOwned("remove_ads"));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 승인된_비소모성은_지급_없이_소유로_맞추고_목록에_없으면_회수한다() => AwaitableTest.Run(async () =>
    {
        _client.Stored.Add(FakeOneStoreClient.Purchase("remove_ads_os", "order-acked", acknowledged: true));
        var sut = NewService();
        var changed = new List<string>();
        sut.OwnedChanged += changed.Add;

        await sut.InitializeAsync();

        Assert.IsTrue(sut.IsOwned("remove_ads"));
        Assert.IsEmpty(_fulfillment.Calls, "이미 확정된 구매를 다시 지급했다");

        // 환불되어 목록에서 빠졌다.
        _client.Stored.Clear();
        await sut.ProcessUnconfirmedAsync();

        Assert.IsFalse(sut.IsOwned("remove_ads"));
        CollectionAssert.AreEqual(new[] { "remove_ads", "remove_ads" }, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원은_구매_조회로_소유를_되살리고_수를_센다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        // 다른 기기에서 산 비소모성.
        _client.Stored.Add(FakeOneStoreClient.Purchase("remove_ads_os", "order-other", acknowledged: true));
        var result = await sut.RestoreAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.RestoredCount);
        Assert.IsTrue(sut.IsOwned("remove_ads"));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원_조회가_실패하면_RestoreFailed로_끝난다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        _client.PurchasesResult = new OneStoreResult(6, "error");
        LogAssert.Expect(LogType.Warning, new Regex("OneStoreIAPService.*구매 조회"));
        var result = await sut.RestoreAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(IAPErrorCodes.RestoreFailed, result.Error.Code);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지급_처리기가_준비되지_않으면_보류했다가_ProcessUnconfirmedAsync로_지급한다() => AwaitableTest.Run(async () =>
    {
        _client.Stored.Add(FakeOneStoreClient.Purchase("gems_os", "order-old"));
        var gated = new GatedFulfillment { IsReady = false };
        var sut = NewService(gated);

        await sut.InitializeAsync();
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(gated.Calls);
        Assert.IsEmpty(_client.Consumed);

        gated.IsReady = true;
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, gated.Calls.Count, "보류 목록과 재조회가 같은 구매를 두 번 지급했다");
        Assert.AreEqual(1, _client.Consumed.Count);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정이_반영되기_전에_조회에_다시_와도_두_번_지급하지_않는다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("remove_ads");
        var purchase = FakeOneStoreClient.Purchase("remove_ads_os", "order-1");
        _client.CompletePurchase(purchase);
        await buying;

        // 승인이 아직 반영되지 않은 스토어.
        _client.Stored.Clear();
        _client.Stored.Add(purchase);
        await sut.ProcessUnconfirmedAsync();
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, _fulfillment.Calls.Count);
        Assert.IsTrue(sut.IsOwned("remove_ads"), "지급 중인 구매의 소유를 회수했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator ProcessUnconfirmedAsync를_연달아_불러도_조회는_한_번만_한다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();
        var before = _client.QueryPurchasesCount;

        _client.HoldPurchases = true;
        var first = sut.ProcessUnconfirmedAsync();
        var second = sut.ProcessUnconfirmedAsync();
        _client.ReleaseQueries();

        await first;
        await second;

        Assert.AreEqual(before + 1, _client.QueryPurchasesCount);
    });

    // ── 해제 ──────────────────────────────────────────────────

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 해제하면_대기_중인_구매가_Disposed로_끝나고_클라이언트를_닫는다() => AwaitableTest.Run(async () =>
    {
        var sut = NewService();
        await sut.InitializeAsync();

        var buying = sut.PurchaseAsync("gems");
        sut.Dispose();
        var result = await buying;

        Assert.AreEqual(IAPErrorCodes.Disposed, result.Error.Code);
        Assert.IsTrue(_client.Disposed);
    });

    // ── 가격 문구 ──────────────────────────────────────────────

    [TestCase("1200", 1200.0, "KRW", "₩1,200")]
    [TestCase("1.99", 1.99, "USD", "1.99 USD")]
    [TestCase("₩1,200", 1200.0, "KRW", "₩1,200")]
    public void 가격은_통화에_맞춰_찍고_기호가_있으면_그대로_쓴다(string raw, double price, string currency, string expected)
    {
        Assert.AreEqual(expected, OneStoreIAPService.FormatPrice(raw, price, currency));
    }
}
