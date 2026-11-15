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

    private static AdServiceOptions NewOptions(bool initializeWhenConsentUndetermined = false)
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
            autoLoadOnInitialize: true,
            initializeWhenConsentUndetermined: initializeWhenConsentUndetermined);
    }

    private static AdService Create(FakeAdProvider provider, IConsentService consent,
                                    bool initializeWhenConsentUndetermined = false) =>
        new(provider, new FakeAdDispatcher(), NewOptions(initializeWhenConsentUndetermined),
            new FakeRemovalStorage(), consent);

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 옵션이_켜져_있고_판정_미완료면_개인_맞춤을_끈_채_provider를_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("판정하지 못했다"));

        Assert.IsTrue(await Create(provider, consent, initializeWhenConsentUndetermined: true).InitializeAsync(),
                      "첫 실행에서 UMP 갱신이 실패한 유저는 그 세션 내내 광고를 받지 못한다");
        Assert.AreEqual(1, provider.InitializeCallCount);
        Assert.IsTrue(provider.ReceivedContext.IsPersonalizationRestricted,
                      "동의를 판정하지 못한 유저에게 개인 맞춤 광고가 나간다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 옵션이_꺼져_있으면_판정_미완료여도_초기화하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));

        Assert.IsFalse(await Create(provider, consent).InitializeAsync());
        Assert.AreEqual(0, provider.InitializeCallCount, "옵션을 켜지 않은 게임의 동작이 바뀌었다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 옵션이_켜져_있어도_답변이_필요하면_초기화하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Required() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));

        Assert.IsFalse(await Create(provider, consent, initializeWhenConsentUndetermined: true).InitializeAsync());
        Assert.AreEqual(0, provider.InitializeCallCount, "동의 창에 답하지 않은 유저에게 광고를 요청했다");
    });

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
    public void 개인_맞춤_제한을_실으면_추적이_허용이어도_프라이버시_제한으로_본다()
    {
        var context = new AdProviderContext("app-key", false, false, new List<string>())
            .WithTrackingStatus(TrackingAuthorizationStatus.Authorized)
            .WithPersonalizationRestricted();

        Assert.IsTrue(context.IsPrivacyRestricted, "동의를 판정하지 못한 유저에게 개인 맞춤 광고가 나간다");
        Assert.IsFalse(context.IsTrackingRestricted);
    }

    [Test]
    public void 추적_거부와_제한도_프라이버시_제한으로_본다()
    {
        var context = new AdProviderContext("app-key", false, false, new List<string>());

        Assert.IsTrue(context.WithTrackingStatus(TrackingAuthorizationStatus.Denied).IsPrivacyRestricted);
        Assert.IsTrue(context.WithTrackingStatus(TrackingAuthorizationStatus.Restricted).IsPrivacyRestricted);
        Assert.IsFalse(context.IsPrivacyRestricted, "ATT가 없는 플랫폼에서 비개인화로 떨어지면 수익이 죽는다");
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

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의가_나중에_확정되면_스스로_다시_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));
        Assert.IsFalse(await sut.InitializeAsync());

        consent.RaiseChanged(ConsentResults.Granted());

        Assert.AreEqual(1, provider.InitializeCallCount, "오프라인 첫 실행 뒤 네트워크가 돌아와도 광고가 세션 내내 죽어 있다");
        Assert.IsTrue(sut.IsInitialized);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose_뒤의_동의_변경은_무시한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));
        await sut.InitializeAsync();
        sut.Dispose();

        consent.RaiseChanged(ConsentResults.Granted());

        Assert.AreEqual(0, provider.InitializeCallCount);
    });
}
