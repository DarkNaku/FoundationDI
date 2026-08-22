using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
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
    public IEnumerator 초기화하면_provider_상품이_노출된다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 초기화에_실패하면_상품이_비고_구매는_NotReady다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 초기화_전_구매는_NotReady다() => UniTask.ToCoroutine(async () =>
    {
        var sut = NewService(new FakeIapProvider());

        var result = await sut.PurchaseAsync("gems");

        Assert.AreEqual(IapPurchaseOutcome.NotReady, result.Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator InitializeAsync는_재진입해도_provider를_한_번만_초기화한다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 구매가_검증_지급_확정_순서로_진행된다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 지급이_실패하면_확정하지_않는다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 지급이_예외를_던져도_확정하지_않고_서비스가_살아있다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 영수증_검증에_실패하면_지급도_확정도_하지_않는다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 비소모성은_확정_후_소유로_기록되고_소모성은_아니다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 카탈로그에_없는_구매가_도착하면_확정하지_않고_경고한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeIapProvider();
        var fulfillment = new FakeFulfillment();
        var sut = NewService(provider, fulfillment);
        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        provider.RaisePending(new IapPendingPurchase("unknown_store", "tx-x", "r", false));
        await UniTask.Yield();

        Assert.IsEmpty(fulfillment.Calls);
        Assert.IsEmpty(provider.ConfirmCalls, "지급할 수 없는 구매를 확정하면 영영 되찾을 수 없다");
    });
}
