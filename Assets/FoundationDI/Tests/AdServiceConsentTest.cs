using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AdServiceConsentTest
{
    private class FakeRemovalStorage : IAdRemovalStorage
    {
        public bool Load() => false;
        public void Save(bool removed) { }
    }

    private static AdServiceOptions NewOptions()
    {
        return new AdServiceOptions(
            banner: new AdUnitId("banner-a", "banner-i"),
            interstitial: new AdUnitId("inter-a", "inter-i"),
            rewarded: new AdUnitId("reward-a", "reward-i"),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext("app-key", false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            interstitialCooldownSeconds: 0f,
            autoLoadOnInitialize: true);
    }

    private static AdService Create(FakeAdProvider provider, IConsentService consent) =>
        new(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage(), consent);

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의를_먼저_받고_provider를_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Defer = true };
        var sut = Create(provider, consent);

        var initializing = sut.InitializeAsync();

        Assert.AreEqual(1, consent.RequestCount);
        Assert.AreEqual(0, provider.InitializeCallCount, "동의 전에 광고 SDK를 초기화했다");

        consent.Complete(ConsentResults.Granted());

        Assert.IsTrue(await initializing);
        Assert.AreEqual(1, provider.InitializeCallCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고를_요청할_수_없으면_provider를_초기화하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));

        Assert.IsFalse(await Create(provider, consent).InitializeAsync());
        Assert.AreEqual(0, provider.InitializeCallCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관을_수락하지_않았으면_초기화하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.PolicyNotAccepted() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));

        Assert.IsFalse(await Create(provider, consent).InitializeAsync());
        Assert.AreEqual(0, provider.InitializeCallCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 거부된_뒤_다시_부르면_동의부터_재시도한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));
        await sut.InitializeAsync();

        consent.Result = ConsentResults.Granted();

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(2, consent.RequestCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_결과의_추적_거부를_provider가_받는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Granted(TrackingAuthorizationStatus.Denied) };

        await Create(provider, consent).InitializeAsync();

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, provider.ReceivedContext.TrackingStatus);
        Assert.IsTrue(provider.ReceivedContext.IsTrackingRestricted,
                      "거부를 provider가 모르면 SDK는 동의받은 것으로 간주하고 계속 추적한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 물을_수_없는_플랫폼은_제한으로_보지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();

        await Create(provider, new FakeConsentService()).InitializeAsync();

        Assert.IsFalse(provider.ReceivedContext.IsTrackingRestricted,
                       "ATT가 없는 플랫폼에서 추적을 끄면 이유 없이 수익이 죽는다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의를_넘기지_않으면_추적만_묻는_기본_동의로_동작한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, provider.InitializeCallCount);
    });

    [Test]
    public void 거부와_제한만_추적_제한으로_본다()
    {
        var context = new AdProviderContext("app-key", false, false, new List<string>());

        Assert.IsFalse(context.WithTrackingStatus(TrackingAuthorizationStatus.Authorized).IsTrackingRestricted);
        Assert.IsTrue(context.WithTrackingStatus(TrackingAuthorizationStatus.Denied).IsTrackingRestricted);
        Assert.IsTrue(context.WithTrackingStatus(TrackingAuthorizationStatus.Restricted).IsTrackingRestricted);
    }

    [Test]
    public void 묻지_않은_상태는_제한으로_보지_않는다()
    {
        var context = new AdProviderContext("app-key", false, false, new List<string>());

        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, context.TrackingStatus);
        Assert.IsFalse(context.IsTrackingRestricted);
    }

    [Test]
    public void 추적_상태를_바꿔도_나머지_설정은_그대로다()
    {
        var ids = new List<string> { "device-a" };
        var context = new AdProviderContext("app-key", true, true, ids)
            .WithTrackingStatus(TrackingAuthorizationStatus.Denied);

        Assert.AreEqual("app-key", context.AppKey);
        Assert.IsTrue(context.VerboseLogging);
        Assert.IsTrue(context.TestMode);
        Assert.AreSame(ids, context.TestDeviceIds);
    }
}
