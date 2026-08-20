using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AdServiceTest
{
    private class FakeRemovalStorage : IAdRemovalStorage
    {
        public bool Value;
        public int SaveCount;
        public bool Load() => Value;
        public void Save(bool removed) { Value = removed; SaveCount++; }
    }

    private static AdServiceOptions NewOptions(bool autoLoad = true)
    {
        return new AdServiceOptions(
            banner: new AdUnitId("banner-a", "banner-i"),
            interstitial: new AdUnitId("inter-a", "inter-i"),
            rewarded: new AdUnitId("reward-a", "reward-i"),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext("app-key", false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            autoLoadOnInitialize: autoLoad);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_성공하면_IsInitialized가_참이_되고_전면과_보상을_로드한다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        Assert.IsFalse(sut.IsInitialized, "초기화 전인데 IsInitialized가 참이다");

        var ok = await sut.InitializeAsync();

        Assert.IsTrue(ok);
        Assert.IsTrue(sut.IsInitialized);
        Assert.AreEqual("app-key", provider.ReceivedContext.AppKey);
        Assert.AreEqual(1, provider.InterstitialAdapter.LoadCount, "전면을 미리 로드하지 않았다");
        Assert.AreEqual(1, provider.RewardedAdapter.LoadCount, "보상을 미리 로드하지 않았다");
    });
}
