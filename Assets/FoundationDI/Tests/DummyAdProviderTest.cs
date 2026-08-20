using System;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class DummyAdProviderTest
{
    private class FakeScreen : IDummyAdScreen
    {
        public int FullScreenCount;
        public int BannerShowCount;
        public int BannerHideCount;
        public Action OnSkip;
        public Action OnComplete;
        public bool IsDisposed;

        public void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete)
        {
            FullScreenCount++;
            OnSkip = onSkip;
            OnComplete = onComplete;
        }

        public void ShowBanner(BannerPosition position, float height) => BannerShowCount++;
        public void HideBanner() => BannerHideCount++;
        public void Dispose() => IsDisposed = true;
    }

    private static readonly DummyAdOptions NeverFails =
        new(loadDelaySeconds: 1f, failureRate: 0f, adDurationSeconds: 3f, bannerHeight: 100f);

    [Test]
    public void 더미_전면광고는_설정된_지연_후_로드에_성공한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, "unit-1", dispatcher, screen, NeverFails, () => 0.5f);

        var loaded = 0;
        sut.Loaded += () => loaded++;

        sut.Load();
        Assert.AreEqual(0, loaded, "지연 없이 즉시 로드됐다");
        Assert.IsFalse(sut.IsReady);

        dispatcher.Advance(1.1f);

        Assert.AreEqual(1, loaded);
        Assert.IsTrue(sut.IsReady);
    }

    [Test]
    public void 더미_광고는_실패율이_1이면_로드에_실패한다()
    {
        var options = new DummyAdOptions(1f, failureRate: 1f, adDurationSeconds: 3f, bannerHeight: 100f);
        var dispatcher = new FakeAdDispatcher();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, "unit-1", dispatcher, new FakeScreen(),
                                             options, () => 0.5f);

        AdError? failed = null;
        sut.LoadFailed += e => failed = e;

        sut.Load();
        dispatcher.Advance(1.1f);

        Assert.IsTrue(failed.HasValue, "실패율 1인데 로드에 성공했다");
        Assert.IsFalse(sut.IsReady);
    }

    [Test]
    public void 더미_보상광고는_완주하면_보상_후_닫힘을_발화한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Rewarded, "unit-1", dispatcher, screen, NeverFails, () => 0.5f);

        var order = new System.Collections.Generic.List<string>();
        sut.Rewarded += _ => order.Add("rewarded");
        sut.Closed += () => order.Add("closed");

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();

        Assert.AreEqual(1, screen.FullScreenCount, "화면이 요청되지 않았다");

        screen.OnComplete();

        CollectionAssert.AreEqual(new[] { "rewarded", "closed" }, order);
    }

    [Test]
    public void 더미_보상광고는_중간에_닫으면_보상없이_닫힘만_발화한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Rewarded, "unit-1", dispatcher, screen, NeverFails, () => 0.5f);

        var rewarded = 0;
        var closed = 0;
        sut.Rewarded += _ => rewarded++;
        sut.Closed += () => closed++;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();
        screen.OnSkip();

        Assert.AreEqual(0, rewarded, "중간에 닫았는데 보상이 나왔다");
        Assert.AreEqual(1, closed);
    }

    [Test]
    public void 더미_전면광고는_인터스티셜이면_완주해도_보상을_지급하지_않는다()
    {
        // Show()의 onComplete 안 "if (_format == AdFormat.Rewarded)" 가드가 사라져도
        // 다른 테스트는 전부 통과한다 — 이 테스트만 그 가드를 직접 고정한다.
        // 인터스티셜이 보상을 지급하면 재화 악용 경로가 된다.
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, "unit-1", dispatcher, screen, NeverFails, () => 0.5f);

        var rewarded = 0;
        var closed = 0;
        sut.Rewarded += _ => rewarded++;
        sut.Closed += () => closed++;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();
        screen.OnComplete();

        Assert.AreEqual(0, rewarded, "인터스티셜이 완주했는데 보상이 나왔다");
        Assert.AreEqual(1, closed);
    }

    [Test]
    public void 더미_광고는_표시할_때_Displayed를_발화한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, "unit-1", dispatcher, new FakeScreen(),
                                             NeverFails, () => 0.5f);

        var displayed = 0;
        sut.Displayed += () => displayed++;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();

        Assert.AreEqual(1, displayed);
    }

    [Test]
    public void 더미_광고는_표시할_때_임프레션을_발행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, "unit-1", dispatcher, new FakeScreen(),
                                             NeverFails, () => 0.5f);

        AdImpression? impression = null;
        sut.Paid += imp => impression = imp;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();

        Assert.IsTrue(impression.HasValue, "임프레션이 발행되지 않았다");
        Assert.AreEqual("Dummy", impression.Value.AdPlatform);
        Assert.AreEqual("DummyNetwork", impression.Value.NetworkName);
        Assert.AreEqual("USD", impression.Value.Currency);
        Assert.Greater(impression.Value.Revenue, 0.0);
    }

    [Test]
    public void 더미_광고를_준비도_안_된_상태에서_표시하면_표시_실패를_발화한다()
    {
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, "unit-1", new FakeAdDispatcher(),
                                             new FakeScreen(), NeverFails, () => 0.5f);

        AdError? failed = null;
        sut.DisplayFailed += e => failed = e;

        sut.Show();

        Assert.IsTrue(failed.HasValue);
    }

    [Test]
    public void 더미_배너는_표시하면_설정된_높이를_보고하고_임프레션을_발행한다()
    {
        var screen = new FakeScreen();
        var sut = new DummyBannerAdapter(screen, "unit-1",
            new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true), NeverFails);

        var reported = -1f;
        AdImpression? impression = null;
        sut.HeightChanged += h => reported = h;
        sut.Paid += imp => impression = imp;

        sut.Show();

        Assert.AreEqual(1, screen.BannerShowCount);
        Assert.AreEqual(100f, sut.Height, 0.001f);
        Assert.AreEqual(100f, reported, 0.001f);
        Assert.IsTrue(impression.HasValue);
        Assert.AreEqual(AdFormat.Banner, impression.Value.Format);

        sut.Hide();

        Assert.AreEqual(1, screen.BannerHideCount);
        Assert.AreEqual(0f, sut.Height, 0.001f);
    }

    [Test]
    public void 더미_provider는_외부에서_받은_화면을_해제하지_않는다()
    {
        // 화면 소유권을 잘못 잡으면 provider 재생성 시 남의 Canvas를 파괴한다.
        var screen = new FakeScreen();
        var sut = new DummyAdProvider(new FakeAdDispatcher(), NeverFails, screen);

        sut.Dispose();

        Assert.IsFalse(screen.IsDisposed);
    }

    [Test]
    public void 더미_provider의_CreateInterstitial은_전달받은_광고단위ID를_임프레션에_싣는다()
    {
        // 합성 문자열("dummy-interstitial")로 채워지면 배치별 실제 유닛 ID가
        // 실기에서 검증되지 않는다 — 이 테스트가 그 회귀를 고정한다.
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var provider = new DummyAdProvider(dispatcher, NeverFails, screen, () => 0.5f);

        var sut = provider.CreateInterstitial("unit-42");

        AdImpression? impression = null;
        sut.Paid += imp => impression = imp;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();

        Assert.IsTrue(impression.HasValue);
        Assert.AreEqual("unit-42", impression.Value.AdUnitId);
    }

    [Test]
    public void 더미_provider의_CreateBanner는_전달받은_광고단위ID를_임프레션에_싣는다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var provider = new DummyAdProvider(dispatcher, NeverFails, screen);

        var sut = provider.CreateBanner("banner-unit-7",
            new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true));

        AdImpression? impression = null;
        sut.Paid += imp => impression = imp;

        sut.Show();

        Assert.IsTrue(impression.HasValue);
        Assert.AreEqual("banner-unit-7", impression.Value.AdUnitId);
    }
}
