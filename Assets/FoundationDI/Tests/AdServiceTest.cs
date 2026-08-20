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

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_실패하면_false를_반환하고_광고를_요청하지_않는다() =>
        UniTask.ToCoroutine(async () =>
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
        UniTask.ToCoroutine(async () =>
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
    public IEnumerator 어댑터_이벤트가_포맷과_함께_서비스_이벤트로_전파된다() => UniTask.ToCoroutine(async () =>
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
        UniTask.ToCoroutine(async () =>
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
    public IEnumerator 광고제거_상태는_저장소에_영속화되고_생성시_복원된다() => UniTask.ToCoroutine(async () =>
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

        await UniTask.Yield();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거를_켜면_배너가_사라진다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator Dispose는_provider와_모든_광고_유닛을_해제한다() => UniTask.ToCoroutine(async () =>
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
    public IEnumerator 초기화_전에_Dispose를_호출해도_안전하다() => UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        Assert.DoesNotThrow(() => sut.Dispose());
        Assert.IsTrue(provider.IsDisposed);
        Assert.IsFalse(sut.IsInitialized);

        await UniTask.Yield();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose_이후에는_InitializeAsync가_거짓을_반환하고_다시_초기화하지_않는다() =>
        UniTask.ToCoroutine(async () =>
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
}
