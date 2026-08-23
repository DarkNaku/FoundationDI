using DarkNaku.FoundationDI;
using NUnit.Framework;

public class IapTypesTest
{
    [Test]
    public void 플랫폼_오버라이드가_비면_공용_ID로_폴백한다()
    {
        var empty = new IapProductId(null, null);

        Assert.AreEqual("remove_ads", empty.Resolve("remove_ads"));
    }

    [Test]
    public void 플랫폼_오버라이드가_있으면_그것을_쓴다()
    {
        var id = new IapProductId("com.game.remove_ads.android", "com.game.remove_ads.ios");

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
        var purchase = new IapPurchase("gems", IapProductType.Consumable, "tx", "receipt", 4.99, "USD", false);

        Assert.IsTrue(IapPurchaseResult.Purchased(purchase).IsSuccess);
        Assert.IsTrue(IapPurchaseResult.Restored(purchase).IsSuccess);
        Assert.IsTrue(IapPurchaseResult.AlreadyOwned(purchase).IsSuccess);

        Assert.IsFalse(IapPurchaseResult.UserCancelled().IsSuccess);
        Assert.IsFalse(IapPurchaseResult.Deferred().IsSuccess);
        Assert.IsFalse(IapPurchaseResult.NotReady().IsSuccess);
        Assert.IsFalse(IapPurchaseResult.InvalidReceipt(new IapError(1, "bad")).IsSuccess);
        Assert.IsFalse(IapPurchaseResult.Failed(new IapError(2, "boom")).IsSuccess);
    }

    [Test]
    public void 구매결과가_결과에_맞는_페이로드만_담는다()
    {
        var purchase = new IapPurchase("gems", IapProductType.Consumable, "tx", "receipt", 4.99, "USD", false);
        var error = new IapError(7, "network");

        var purchased = IapPurchaseResult.Purchased(purchase);
        Assert.AreEqual("gems", purchased.Purchase.ProductId);
        Assert.AreEqual(0, purchased.Error.Code);

        var failed = IapPurchaseResult.Failed(error);
        Assert.AreEqual(7, failed.Error.Code);
        Assert.IsNull(failed.Purchase.ProductId);
    }

    [Test]
    public void 복원결과가_성공과_실패를_구분한다()
    {
        var ok = IapRestoreResult.Ok(3);
        Assert.IsTrue(ok.Success);
        Assert.AreEqual(3, ok.RestoredCount);

        var fail = IapRestoreResult.Fail(new IapError(9, "denied"));
        Assert.IsFalse(fail.Success);
        Assert.AreEqual(0, fail.RestoredCount);
        Assert.AreEqual(9, fail.Error.Code);
    }
}
