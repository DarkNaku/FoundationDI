using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class DummyIapProviderTest
{
    private static readonly IapProductDefinition[] Catalog =
    {
        new("gems", "gems_store", IapProductType.Consumable),
        new("remove_ads", "remove_ads_store", IapProductType.NonConsumable),
    };

    [SetUp]
    [TearDown]
    public void ClearOwned()
    {
        PlayerPrefs.DeleteKey("FoundationDI.IAP.Dummy.Owned.gems_store");
        PlayerPrefs.DeleteKey("FoundationDI.IAP.Dummy.Owned.remove_ads_store");
    }

    // 지연 0으로 두면 이벤트가 동기적으로 발행돼 프레임을 기다릴 필요가 없다.
    private static DummyIapProvider NewProvider(bool alwaysFail = false, bool alwaysCancel = false) =>
        new(new DummyIapOptions(0f, alwaysFail, alwaysCancel, "$0.99"));

    private static IapProviderContext Context() => new(Catalog, false);

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화하면_카탈로그가_가짜_가격으로_노출된다() => AwaitableTest.Run(async () =>
    {
        var provider = NewProvider();

        Assert.IsTrue(await provider.InitializeAsync(Context()));
        Assert.AreEqual(2, provider.Products.Count);
        Assert.AreEqual("gems", provider.Products[0].Id);
        Assert.AreEqual("$0.99", provider.Products[0].LocalizedPrice);
        Assert.IsTrue(provider.Products[0].IsAvailable);

        provider.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 구매하면_미확정_구매가_발행된다() => AwaitableTest.Run(async () =>
    {
        var provider = NewProvider();
        await provider.InitializeAsync(Context());

        var pendings = new List<IapPendingPurchase>();
        provider.PurchasePending += p => pendings.Add(p);

        Assert.IsTrue(provider.Purchase("gems_store"));

        Assert.AreEqual(1, pendings.Count);
        Assert.AreEqual("gems_store", pendings[0].StoreId);
        Assert.IsFalse(pendings[0].IsRestored);
        Assert.IsNotEmpty(pendings[0].TransactionId);

        provider.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 모르는_상품은_구매를_시작하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = NewProvider();
        await provider.InitializeAsync(Context());

        Assert.IsFalse(provider.Purchase("unknown_store"));

        provider.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator AlwaysCancel과_AlwaysFail이_실패_경로를_재현한다() => AwaitableTest.Run(async () =>
    {
        var cancelling = NewProvider(alwaysCancel: true);
        await cancelling.InitializeAsync(Context());

        IapPurchaseFailure? cancelled = null;
        cancelling.PurchaseFailed += f => cancelled = f;
        cancelling.Purchase("gems_store");

        Assert.IsTrue(cancelled.HasValue);
        Assert.IsTrue(cancelled.Value.IsUserCancelled);
        cancelling.Dispose();

        var failing = NewProvider(alwaysFail: true);
        await failing.InitializeAsync(Context());

        IapPurchaseFailure? failure = null;
        failing.PurchaseFailed += f => failure = f;
        failing.Purchase("gems_store");

        Assert.IsTrue(failure.HasValue);
        Assert.IsFalse(failure.Value.IsUserCancelled);
        failing.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정한_비소모성만_복원된다() => AwaitableTest.Run(async () =>
    {
        var provider = NewProvider();
        await provider.InitializeAsync(Context());

        string consumableTx = null;
        string nonConsumableTx = null;
        provider.PurchasePending += p =>
        {
            if (p.StoreId == "gems_store") consumableTx = p.TransactionId;
            else nonConsumableTx = p.TransactionId;
        };

        provider.Purchase("gems_store");
        provider.Purchase("remove_ads_store");
        provider.Confirm(consumableTx);
        provider.Confirm(nonConsumableTx);
        provider.Dispose();

        // 새 인스턴스(= 앱 재시작)에서 복원한다.
        var restored = NewProvider();
        await restored.InitializeAsync(Context());

        var replayed = new List<IapPendingPurchase>();
        restored.PurchasePending += p => replayed.Add(p);

        Assert.IsTrue(await restored.RestoreAsync());
        Assert.AreEqual(1, replayed.Count, "소모성이 복원됐거나 비소모성이 복원되지 않았다");
        Assert.AreEqual("remove_ads_store", replayed[0].StoreId);
        Assert.IsTrue(replayed[0].IsRestored);

        restored.Dispose();
    });
}
