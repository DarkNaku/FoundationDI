using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AdServiceInitStateTest
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
            autoLoadOnInitialize: false,
            initializeWhenConsentUndetermined: initializeWhenConsentUndetermined);
    }

    private static AdService Create(FakeAdProvider provider, IConsentService consent,
                                    bool initializeWhenConsentUndetermined = false) =>
        new(provider, new FakeAdDispatcher(), NewOptions(initializeWhenConsentUndetermined),
            new FakeRemovalStorage(), consent);

    [Test]
    public void 처음_상태는_Pending이다()
    {
        var sut = Create(new FakeAdProvider(), new FakeConsentService { Result = ConsentResults.Granted() });

        Assert.AreEqual(AdInitState.Pending, sut.InitState);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의가_광고를_허용하지_않으면_WaitingConsent다() => AwaitableTest.Run(async () =>
    {
        var sut = Create(new FakeAdProvider(), new FakeConsentService { Result = ConsentResults.Unknown() });

        LogAssert.Expect(LogType.Warning, new Regex("광고를 요청할 수 없다"));

        await sut.InitializeAsync();

        Assert.AreEqual(AdInitState.WaitingConsent, sut.InitState);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator provider_초기화가_성공하면_Ready_실패하면_Failed다() => AwaitableTest.Run(async () =>
    {
        var succeeded = Create(new FakeAdProvider(), new FakeConsentService { Result = ConsentResults.Granted() });
        var failed = Create(new FakeAdProvider { InitializeResult = false },
                            new FakeConsentService { Result = ConsentResults.Granted() });

        LogAssert.Expect(LogType.Error, new Regex("초기화에 실패했다"));

        await succeeded.InitializeAsync();
        await failed.InitializeAsync();

        Assert.AreEqual(AdInitState.Ready, succeeded.InitState);
        Assert.AreEqual(AdInitState.Failed, failed.InitState);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 판정_미완료로_개인_맞춤을_끈_채_초기화하면_ReadyRestricted다() => AwaitableTest.Run(async () =>
    {
        // 0.19.0 InitializeWhenConsentUndetermined가 실제로 얼마나 쓰이는지가 이 값으로 드러난다.
        var sut = Create(new FakeAdProvider(), new FakeConsentService { Result = ConsentResults.Unknown() },
                         initializeWhenConsentUndetermined: true);

        LogAssert.Expect(LogType.Warning, new Regex("판정하지 못했다"));

        await sut.InitializeAsync();

        Assert.AreEqual(AdInitState.ReadyRestricted, sut.InitState);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_대기_중_허용되어_초기화되면_Ready로_바뀌고_상태가_바뀔_때만_알린다() => AwaitableTest.Run(async () =>
    {
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(new FakeAdProvider(), consent);
        var changes = new List<AdInitState>();
        sut.InitStateChanged += changes.Add;

        LogAssert.Expect(LogType.Warning, new Regex("광고를 요청할 수 없다"));
        LogAssert.Expect(LogType.Warning, new Regex("광고를 요청할 수 없다"));

        await sut.InitializeAsync();
        await sut.InitializeAsync();   // 같은 WaitingConsent — 다시 알리지 않는다

        consent.RaiseChanged(ConsentResults.Granted());
        await AwaitableTest.WaitUntil(() => sut.IsInitialized);

        CollectionAssert.AreEqual(new[] { AdInitState.WaitingConsent, AdInitState.Ready }, changes);
    });
}
