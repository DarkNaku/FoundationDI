using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdTypesTest
{
    [Test]
    public void 재시도_지연은_시도횟수에_대해_지수적으로_증가한다()
    {
        var policy = new AdRetryPolicy(maxAttempts: 5, baseSeconds: 2f, maxDelaySeconds: 64f);

        Assert.AreEqual(2f, policy.DelayFor(1), 0.001f);
        Assert.AreEqual(4f, policy.DelayFor(2), 0.001f);
        Assert.AreEqual(8f, policy.DelayFor(3), 0.001f);
    }

    [Test]
    public void 재시도_지연은_최대_지연시간을_넘지_않는다()
    {
        var policy = new AdRetryPolicy(maxAttempts: 10, baseSeconds: 2f, maxDelaySeconds: 10f);

        Assert.AreEqual(8f, policy.DelayFor(3), 0.001f);
        Assert.AreEqual(10f, policy.DelayFor(4), 0.001f);   // 16 → 10으로 클램프
        Assert.AreEqual(10f, policy.DelayFor(9), 0.001f);
    }

    [Test]
    public void 보상_결과는_보상정보를_담고_노출된_것으로_간주된다()
    {
        var result = AdShowResult.Rewarded(new AdReward("coins", 50));

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
        Assert.IsTrue(result.IsRewarded);
        Assert.IsTrue(result.WasShown);
        Assert.AreEqual("coins", result.Reward.Label);
        Assert.AreEqual(50, result.Reward.Amount, 0.001);
    }

    [Test]
    public void 보상없이_닫힘과_정상노출은_노출된_것이지만_보상은_아니다()
    {
        Assert.IsTrue(AdShowResult.Dismissed().WasShown);
        Assert.IsFalse(AdShowResult.Dismissed().IsRewarded);
        Assert.IsTrue(AdShowResult.Shown().WasShown);
        Assert.IsFalse(AdShowResult.Shown().IsRewarded);
    }

    [Test]
    public void 준비안됨_실패_차단은_노출되지_않은_것으로_간주된다()
    {
        Assert.IsFalse(AdShowResult.NotReady().WasShown);
        Assert.IsFalse(AdShowResult.Blocked().WasShown);
        Assert.IsFalse(AdShowResult.Failed(new AdError(3, "no fill")).WasShown);
        Assert.AreEqual(3, AdShowResult.Failed(new AdError(3, "no fill")).Error.Code);
    }

    [Test]
    public void 광고단위ID는_현재_플랫폼에_해당하는_값을_돌려준다()
    {
        var id = new AdUnitId("android-unit", "ios-unit");

#if UNITY_ANDROID
        Assert.AreEqual("android-unit", id.Current);
#elif UNITY_IOS
        Assert.AreEqual("ios-unit", id.Current);
#else
        Assert.IsTrue(string.IsNullOrEmpty(id.Current));
#endif
    }

    [Test]
    public void WithPlacement은_배치명만_바꾸고_나머지_필드는_그대로_보존한다()
    {
        var original = new AdImpression(AdFormat.Rewarded, "AdMob", "Meta", "unit-1", "network-placement",
                                        "original-placement", 1.23, "USD", AdRevenuePrecision.Exact, "creative-9");

        var stamped = original.WithPlacement("new-placement");

        Assert.AreEqual("new-placement", stamped.Placement);
        Assert.AreEqual(original.Format, stamped.Format);
        Assert.AreEqual(original.AdPlatform, stamped.AdPlatform);
        Assert.AreEqual(original.NetworkName, stamped.NetworkName);
        Assert.AreEqual(original.AdUnitId, stamped.AdUnitId);
        Assert.AreEqual(original.NetworkPlacement, stamped.NetworkPlacement);
        Assert.AreEqual(original.Revenue, stamped.Revenue, 0.0001);
        Assert.AreEqual(original.Currency, stamped.Currency);
        Assert.AreEqual(original.Precision, stamped.Precision);
        Assert.AreEqual(original.CreativeId, stamped.CreativeId);
    }
}
