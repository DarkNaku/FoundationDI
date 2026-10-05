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
        var store = new UnityIAPStoreHarness();
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
        var store = new UnityIAPStoreHarness();
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
        var store = new UnityIAPStoreHarness { ConnectSucceeds = false };
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
        var store = new UnityIAPStoreHarness { ProductsFetchSucceeds = false };
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
        var store = new UnityIAPStoreHarness { PurchasesFetchSucceeds = false };
        var sut = NewService(store);

        LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

        // 미확정 구매를 이번 실행에 못 찾을 뿐이다 — 상품을 사는 데는 지장이 없다.
        Assert.IsTrue(await sut.InitializeAsync());
        Assert.IsTrue(sut.IsInitialized);
    });
}
