using System.Collections;
using System.Collections.Generic;
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

    private static AdServiceOptions NewOptions(bool autoLoad = true, float interstitialCooldownSeconds = 0f)
    {
        return new AdServiceOptions(
            banner: new AdUnitId("banner-a", "banner-i"),
            interstitial: new AdUnitId("inter-a", "inter-i"),
            rewarded: new AdUnitId("reward-a", "reward-i"),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext("app-key", false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            interstitialCooldownSeconds: interstitialCooldownSeconds,
            autoLoadOnInitialize: autoLoad);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_성공하면_IsInitialized가_참이_되고_전면과_보상을_로드한다() =>
        AwaitableTest.Run(async () =>
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

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_실패하면_false를_반환하고_광고를_요청하지_않는다() =>
        AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider { InitializeResult = false };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("초기화에 실패"));
        var ok = await sut.InitializeAsync();

        Assert.IsFalse(ok);
        Assert.IsFalse(sut.IsInitialized);
        Assert.AreEqual(0, provider.InterstitialAdapter.LoadCount);
        Assert.AreEqual(0, provider.RewardedAdapter.LoadCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 자동로드가_꺼져있으면_초기화해도_광고를_로드하지_않는다() =>
        AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(),
                                NewOptions(autoLoad: false), new FakeRemovalStorage());

        await sut.InitializeAsync();

        Assert.AreEqual(0, provider.InterstitialAdapter.LoadCount);
        Assert.AreEqual(0, provider.RewardedAdapter.LoadCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 어댑터_이벤트가_포맷과_함께_서비스_이벤트로_전파된다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var dispatcher = new FakeAdDispatcher();
        var sut = new AdService(provider, dispatcher, NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();

        var loaded = new List<AdFormat>();
        var displayed = new List<AdFormat>();
        var closed = new List<AdFormat>();
        sut.Loaded += f => loaded.Add(f);
        sut.Displayed += f => displayed.Add(f);
        sut.Closed += f => closed.Add(f);

        provider.RewardedAdapter.RaiseLoaded();
        var pending = sut.Rewarded.ShowAsync();
        provider.RewardedAdapter.RaiseDisplayed();
        provider.RewardedAdapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        CollectionAssert.Contains(loaded, AdFormat.Rewarded);
        CollectionAssert.Contains(displayed, AdFormat.Rewarded);
        CollectionAssert.Contains(closed, AdFormat.Rewarded);
        CollectionAssert.DoesNotContain(displayed, AdFormat.Interstitial);

        // 위의 DoesNotContain은 전면 배선이 통째로 빠져도 통과한다. 전면도 실제로
        // 포맷 태그와 함께 전파되는지 양성 값으로 확인한다.
        provider.InterstitialAdapter.RaiseLoaded();
        var interstitialPending = sut.Interstitial.ShowAsync();
        provider.InterstitialAdapter.RaiseDisplayed();
        provider.InterstitialAdapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await interstitialPending;

        CollectionAssert.Contains(loaded, AdFormat.Interstitial);
        CollectionAssert.Contains(displayed, AdFormat.Interstitial);
        CollectionAssert.Contains(closed, AdFormat.Interstitial);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 어댑터_임프레션과_provider_전역_임프레션이_모두_Paid로_합류한다() =>
        AwaitableTest.Run(async () =>
    {
        // LevelPlay는 전역 경로, AdMob/MAX는 어댑터 경로를 쓴다. 둘 다 새지 않아야 한다.
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();

        var received = new List<string>();
        sut.Paid += imp => received.Add(imp.NetworkName);

        provider.InterstitialAdapter.RaisePaid(NewImpression(AdFormat.Interstitial, "FromAdapter"));
        provider.RaiseImpressionPaid(NewImpression(AdFormat.Banner, "FromProvider"));

        // 배너 갱신 수익이 어댑터 경로로 올 때도(AdMob/MAX 스타일) 새지 않아야 한다.
        // 배너 어댑터는 Show() 전까지 붙지 않으므로 먼저 표시한다.
        sut.Banner.Show();
        provider.BannerAdapter.RaisePaid(NewImpression(AdFormat.Banner, "FromBannerAdapter"));

        CollectionAssert.AreEquivalent(
            new[] { "FromAdapter", "FromProvider", "FromBannerAdapter" }, received);
    });

    private static AdImpression NewImpression(AdFormat format, string network)
    {
        return new AdImpression(format, "Fake", network, "unit", "inst", "place",
                                0.01, "USD", AdRevenuePrecision.Estimated, null);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거_상태는_저장소에_영속화되고_생성시_복원된다() => AwaitableTest.Run(async () =>
    {
        var storage = new FakeRemovalStorage { Value = true };
        var sut = new AdService(new FakeAdProvider(), new FakeAdDispatcher(), NewOptions(), storage);

        Assert.IsTrue(sut.AdsRemoved, "저장된 광고제거 상태가 복원되지 않았다");

        var changes = new List<bool>();
        sut.AdsRemovedChanged += v => changes.Add(v);

        sut.AdsRemoved = false;

        Assert.IsFalse(storage.Value, "저장소에 반영되지 않았다");
        CollectionAssert.AreEqual(new[] { false }, changes);

        var saveCountBefore = storage.SaveCount;
        sut.AdsRemoved = false;   // 같은 값 재설정
        Assert.AreEqual(saveCountBefore, storage.SaveCount, "값이 안 바뀌었는데 저장했다");
        Assert.AreEqual(1, changes.Count, "값이 안 바뀌었는데 이벤트를 쐈다");

        await AwaitableTest.NextFrame();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거를_켜면_배너가_사라진다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();

        sut.Banner.Show();
        Assert.IsTrue(sut.Banner.IsVisible, "배너를 표시했는데 보이지 않는다");

        sut.AdsRemoved = true;

        Assert.IsFalse(sut.Banner.IsVisible, "광고제거 후에도 배너가 보인다고 한다");
        Assert.AreEqual(0f, sut.Banner.Height, "광고제거 후에도 높이가 남아있다");
        Assert.IsTrue(provider.BannerAdapter.IsDisposed, "광고제거가 배너 어댑터까지 전달되지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose는_provider와_모든_광고_유닛을_해제한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();
        sut.Banner.Show();

        var received = new List<string>();
        sut.Paid += imp => received.Add(imp.NetworkName);

        sut.Dispose();

        Assert.IsTrue(provider.IsDisposed, "provider가 해제되지 않았다");
        Assert.IsTrue(provider.InterstitialAdapter.IsDisposed);
        Assert.IsTrue(provider.RewardedAdapter.IsDisposed);
        Assert.IsTrue(provider.BannerAdapter.IsDisposed, "배너 어댑터가 해제되지 않았다");
        Assert.IsFalse(sut.IsInitialized);

        // Dispose가 provider.ImpressionPaid 구독을 해제하지 않으면 해제된 서비스가
        // 계속 수익 이벤트를 공개 Paid로 흘려보낸다.
        provider.RaiseImpressionPaid(NewImpression(AdFormat.Banner, "AfterDispose"));
        Assert.IsEmpty(received, "Dispose 후에도 provider 임프레션 구독이 남아있다");

        Assert.DoesNotThrow(() => sut.Dispose(), "Dispose를 두 번 호출하면 예외가 발생한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전에_Dispose를_호출해도_안전하다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        Assert.DoesNotThrow(() => sut.Dispose());
        Assert.IsTrue(provider.IsDisposed);
        Assert.IsFalse(sut.IsInitialized);

        await AwaitableTest.NextFrame();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose_이후에는_InitializeAsync가_거짓을_반환하고_다시_초기화하지_않는다() =>
        AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var storage = new FakeRemovalStorage();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), storage);
        await sut.InitializeAsync();
        sut.Dispose();

        var ok = await sut.InitializeAsync();

        Assert.IsFalse(ok, "Dispose 후 InitializeAsync가 성공을 반환했다");
        Assert.IsFalse(sut.IsInitialized);
        Assert.IsNull(sut.Interstitial, "Dispose 후에도 전면 광고 유닛이 살아있다");
        Assert.IsNull(sut.Rewarded, "Dispose 후에도 보상 광고 유닛이 살아있다");
        Assert.IsNull(sut.Banner, "Dispose 후에도 배너 유닛이 살아있다");

        var saveCountBefore = storage.SaveCount;
        sut.AdsRemoved = true;
        Assert.AreEqual(saveCountBefore, storage.SaveCount, "Dispose 후에도 AdsRemoved 설정이 저장소에 반영됐다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_진행중에_다시_호출하면_새로_시작하지_않고_같은_결과에_편승한다() =>
        AwaitableTest.Run(async () =>
    {
        // 부트스트랩 시퀀스와 UI 화면이 같은 프레임에 각각 초기화를 기다리는 상황.
        var provider = new FakeAdProvider { DeferInitialize = true };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        var first = sut.InitializeAsync();
        var second = sut.InitializeAsync();

        Assert.AreEqual(1, provider.InitializeCallCount,
                        "초기화가 진행 중인데 provider 초기화를 새로 시작했다");

        provider.CompleteInitialize(true);

        Assert.IsTrue(await first);
        Assert.IsTrue(await second, "편승한 두 번째 호출이 같은 결과를 받지 못했다");

        // 중복 Load는 세 SDK 모두에서 오류다.
        Assert.AreEqual(1, provider.InterstitialAdapter.LoadCount, "전면을 중복 로드했다");
        Assert.AreEqual(1, provider.RewardedAdapter.LoadCount, "보상을 중복 로드했다");

        // 이중 구독은 오직 여기서만 드러난다. provider 임프레션 한 번에 Paid가 두 번 발화하면
        // 분석에 광고 수익이 2배로 기록된다.
        var received = new List<string>();
        sut.Paid += imp => received.Add(imp.NetworkName);

        provider.RaiseImpressionPaid(NewImpression(AdFormat.Banner, "Once"));

        CollectionAssert.AreEqual(new[] { "Once" }, received,
                                  "provider.ImpressionPaid 구독이 중복돼 Paid가 여러 번 발화했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_시_비어있는_광고단위ID는_포맷명과_함께_에러_로그를_남긴다() =>
        AwaitableTest.Run(async () =>
    {
        // AdUnitId.IsValid가 있어도 아무도 부르지 않으면 빈 ID가 그대로 SDK로 넘어가
        // 원인이 안 보이는 로드 에러로만 드러난다. 여기서 미리 잡아 포맷명을 남긴다.
        var provider = new FakeAdProvider();
        var options = new AdServiceOptions(
            banner: new AdUnitId("banner-a", "banner-i"),
            interstitial: new AdUnitId("", ""),   // 설정 누락
            rewarded: new AdUnitId("reward-a", "reward-i"),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext("app-key", false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            interstitialCooldownSeconds: 0f,
            autoLoadOnInitialize: false);
        var sut = new AdService(provider, new FakeAdDispatcher(), options, new FakeRemovalStorage());

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("Interstitial"));
        await sut.InitializeAsync();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dummy_provider는_비어있는_광고단위ID여도_에러를_남기지_않는다() =>
        AwaitableTest.Run(async () =>
    {
        // Dummy provider는 미설정 ID로 정상 동작하도록 설계됐다 — 실기 스모크마다
        // 매번 에러 로그가 찍히면 안 된다.
        var provider = new FakeAdProvider { Name = "Dummy" };
        var options = new AdServiceOptions(
            banner: new AdUnitId("", ""),
            interstitial: new AdUnitId("", ""),
            rewarded: new AdUnitId("", ""),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext(null, false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            interstitialCooldownSeconds: 0f,
            autoLoadOnInitialize: false);
        var sut = new AdService(provider, new FakeAdDispatcher(), options, new FakeRemovalStorage());

        var ok = await sut.InitializeAsync();   // 예기치 못한 에러 로그가 있으면 LogAssert가 실패시킨다

        Assert.IsTrue(ok);
        Assert.IsTrue(sut.IsInitialized);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_실패한_뒤_다시_호출하면_새로_시도한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider { DeferInitialize = true };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("초기화에 실패"));

        var first = sut.InitializeAsync();
        provider.CompleteInitialize(false);

        Assert.IsFalse(await first);
        Assert.IsFalse(sut.IsInitialized);

        // 실패 경로에서 진행 플래그를 내리지 않으면 이 두 번째 호출은 영영 완료되지 않는다.
        provider.DeferInitialize = false;
        var ok = await sut.InitializeAsync();

        Assert.IsTrue(ok, "초기화 실패 후 재시도가 성공하지 못했다");
        Assert.IsTrue(sut.IsInitialized);
        Assert.AreEqual(2, provider.InitializeCallCount, "재시도가 provider 초기화를 다시 부르지 않았다");
        Assert.IsNotNull(sut.Interstitial, "재시도 후에도 전면 광고 유닛이 만들어지지 않았다");
        Assert.IsNotNull(sut.Rewarded, "재시도 후에도 보상 광고 유닛이 만들어지지 않았다");
        Assert.IsNotNull(sut.Banner, "재시도 후에도 배너 유닛이 만들어지지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 설정된_전면_쿨다운은_보상형에는_적용되지_않는다() => AwaitableTest.Run(async () =>
    {
        // BuildAdUnits이 옵션의 쿨다운을 전면에만 흘려보내고 보상에는 0을 넘기는지 확인한다.
        var provider = new FakeAdProvider();
        var dispatcher = new FakeAdDispatcher();
        var sut = new AdService(provider, dispatcher,
                                NewOptions(interstitialCooldownSeconds: 120f), new FakeRemovalStorage());
        await sut.InitializeAsync();

        // 전면: 표시하고 닫는다 — 쿨다운이 걸려야 한다.
        provider.InterstitialAdapter.RaiseLoaded();
        var interstitialPending = sut.Interstitial.ShowAsync();
        provider.InterstitialAdapter.RaiseDisplayed();
        provider.InterstitialAdapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await interstitialPending;

        provider.InterstitialAdapter.RaiseLoaded();   // 닫힘 후 자동 재로드가 끝났다고 가정한다
        Assert.IsFalse(sut.Interstitial.CanShow, "설정된 쿨다운이 전면광고에 적용되지 않았다");

        // 보상: 표시하고 닫는다 — 전면과 같은 쿨다운 설정값을 받았다면 여기도 막혀야 한다.
        provider.RewardedAdapter.RaiseLoaded();
        var rewardedPending = sut.Rewarded.ShowAsync();
        provider.RewardedAdapter.RaiseDisplayed();
        provider.RewardedAdapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await rewardedPending;

        provider.RewardedAdapter.RaiseLoaded();
        Assert.IsTrue(sut.Rewarded.CanShow, "전면 쿨다운이 보상형에도 적용됐다");

        // 매출 보호: bool 하나가 아니라 실제로 두 번째 보상형 표시가 진행되는지 본다.
        // 쿨다운이 새어들어왔다면 여기서 Show가 호출되지 않고 즉시 Blocked로 완료된다.
        var showCountBefore = provider.RewardedAdapter.ShowCount;
        var secondRewardedPending = sut.Rewarded.ShowAsync();

        Assert.AreEqual(showCountBefore + 1, provider.RewardedAdapter.ShowCount,
                        "전면 쿨다운이 보상형의 두 번째 표시를 막았다 — 매출 손실");

        // 정리
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        provider.RewardedAdapter.RaiseDisplayFailed(new AdError(0, "cleanup"));
        await secondRewardedPending;
    });
}
