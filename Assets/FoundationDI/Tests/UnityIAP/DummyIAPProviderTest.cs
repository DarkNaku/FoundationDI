using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class DummyIAPProviderTest
{
    private static readonly IAPProductDefinition[] Catalog =
    {
        new("gems", "gems_store", IAPProductType.Consumable),
        new("remove_ads", "remove_ads_store", IAPProductType.NonConsumable),
    };

    [SetUp]
    [TearDown]
    public void ClearOwned()
    {
        PlayerPrefs.DeleteKey("FoundationDI.IAP.Dummy.Owned.gems_store");
        PlayerPrefs.DeleteKey("FoundationDI.IAP.Dummy.Owned.remove_ads_store");
    }

    // 지연 0으로 두면 이벤트가 동기적으로 발행돼 프레임을 기다릴 필요가 없다.
    private static DummyIAPProvider NewProvider(bool alwaysFail = false, bool alwaysCancel = false) =>
        new(new DummyIAPOptions(0f, alwaysFail, alwaysCancel, "$0.99"));

    private static IAPProviderContext Context() => new(Catalog, false);

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

        var pendings = new List<IAPPendingPurchase>();
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

        IAPPurchaseFailure? cancelled = null;
        cancelling.PurchaseFailed += f => cancelled = f;
        cancelling.Purchase("gems_store");

        Assert.IsTrue(cancelled.HasValue);
        Assert.IsTrue(cancelled.Value.IsUserCancelled);
        cancelling.Dispose();

        var failing = NewProvider(alwaysFail: true);
        await failing.InitializeAsync(Context());

        IAPPurchaseFailure? failure = null;
        failing.PurchaseFailed += f => failure = f;
        failing.Purchase("gems_store");

        Assert.IsTrue(failure.HasValue);
        Assert.IsFalse(failure.Value.IsUserCancelled);
        failing.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 복원은_지급을_다시_거치지_않고_확정한_비소모성만_보고한다() => AwaitableTest.Run(async () =>
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

        var replayed = new List<IAPPendingPurchase>();
        restored.PurchasePending += p => replayed.Add(p);

        IReadOnlyCollection<string> owned = null;
        restored.OwnedProductsFetched += ids => owned = ids;

        Assert.IsTrue(await restored.RestoreAsync());

        // 실제 스토어처럼 확정된 구매는 미확정으로 되돌아오지 않는다 — 이미 지급됐기 때문이다.
        Assert.IsEmpty(replayed, "확정된 구매가 지급 파이프라인으로 다시 들어왔다");
        Assert.IsNotNull(owned, "복원에서 소유 목록을 보고하지 않았다");
        CollectionAssert.AreEquivalent(new[] { "remove_ads_store" }, owned, "소모성이 복원됐거나 비소모성이 복원되지 않았다");

        restored.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_때_확정된_비소모성_목록을_보고한다() => AwaitableTest.Run(async () =>
    {
        var provider = NewProvider();
        await provider.InitializeAsync(Context());

        string tx = null;
        provider.PurchasePending += p => tx = p.TransactionId;
        provider.Purchase("remove_ads_store");
        provider.Confirm(tx);
        provider.Dispose();

        // 새 인스턴스(= 앱 재시작). 실제 스토어처럼 초기화 조회에서 확정 목록이 온다.
        var restarted = NewProvider();

        IReadOnlyCollection<string> owned = null;
        restarted.OwnedProductsFetched += ids => owned = ids;

        await restarted.InitializeAsync(Context());

        Assert.IsNotNull(owned, "초기화에서 소유 목록을 보고하지 않았다");
        CollectionAssert.AreEquivalent(new[] { "remove_ads_store" }, owned);

        restarted.Dispose();
    });
}
