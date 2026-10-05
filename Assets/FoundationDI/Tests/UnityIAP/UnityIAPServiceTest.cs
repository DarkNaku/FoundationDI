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
}
