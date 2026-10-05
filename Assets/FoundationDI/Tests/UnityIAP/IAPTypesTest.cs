using DarkNaku.FoundationDI;
using NUnit.Framework;

public class IAPTypesTest
{
    [Test]
    public void 플랫폼_오버라이드가_비면_공용_ID로_폴백한다()
    {
        var empty = new IAPProductId(null, null);

        Assert.AreEqual("remove_ads", empty.Resolve("remove_ads"));
    }

    [Test]
    public void 플랫폼_오버라이드가_있으면_그것을_쓴다()
    {
        var id = new IAPProductId("com.game.remove_ads.android", "com.game.remove_ads.ios");

#if UNITY_ANDROID
        Assert.AreEqual("com.game.remove_ads.android", id.Resolve("remove_ads"));
#elif UNITY_IOS
        Assert.AreEqual("com.game.remove_ads.ios", id.Resolve("remove_ads"));
#else
        // 모바일 타깃이 아니면 오버라이드를 읽을 방법이 없다 — 공용 ID가 답이어야 한다.
        Assert.AreEqual("remove_ads", id.Resolve("remove_ads"));
#endif
    }

    [Test]
    public void 구매결과의_IsSuccess가_성공_결과에서만_참이다()
    {
        var purchase = new IAPPurchase("gems", IAPProductType.Consumable, "tx", "receipt", 4.99, "USD", false);

        Assert.IsTrue(IAPPurchaseResult.Purchased(purchase).IsSuccess);
        Assert.IsTrue(IAPPurchaseResult.Restored(purchase).IsSuccess);
        Assert.IsTrue(IAPPurchaseResult.AlreadyOwned(purchase).IsSuccess);

        Assert.IsFalse(IAPPurchaseResult.UserCancelled().IsSuccess);
        Assert.IsFalse(IAPPurchaseResult.Deferred().IsSuccess);
        Assert.IsFalse(IAPPurchaseResult.NotReady().IsSuccess);
        Assert.IsFalse(IAPPurchaseResult.InvalidReceipt(new IAPError(1, "bad")).IsSuccess);
        Assert.IsFalse(IAPPurchaseResult.Failed(new IAPError(2, "boom")).IsSuccess);
    }

    [Test]
    public void 구매결과가_결과에_맞는_페이로드만_담는다()
    {
        var purchase = new IAPPurchase("gems", IAPProductType.Consumable, "tx", "receipt", 4.99, "USD", false);
        var error = new IAPError(7, "network");

        var purchased = IAPPurchaseResult.Purchased(purchase);
        Assert.AreEqual("gems", purchased.Purchase.ProductId);
        Assert.AreEqual(0, purchased.Error.Code);

        var failed = IAPPurchaseResult.Failed(error);
        Assert.AreEqual(7, failed.Error.Code);
        Assert.IsNull(failed.Purchase.ProductId);
    }

    [Test]
    public void 복원결과가_성공과_실패를_구분한다()
    {
        var ok = IAPRestoreResult.Ok(3);
        Assert.IsTrue(ok.Success);
        Assert.AreEqual(3, ok.RestoredCount);

        var fail = IAPRestoreResult.Fail(new IAPError(9, "denied"));
        Assert.IsFalse(fail.Success);
        Assert.AreEqual(0, fail.RestoredCount);
        Assert.AreEqual(9, fail.Error.Code);
    }
}
