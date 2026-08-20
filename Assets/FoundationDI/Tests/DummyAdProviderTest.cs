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
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, dispatcher, screen, NeverFails, () => 0.5f);

        var loaded = 0;
        sut.Loaded += () => loaded++;

        sut.Load();
        Assert.AreEqual(0, loaded, "지연 없이 즉시 로드됐다");
        Assert.IsFalse(sut.IsReady);

        dispatcher.Advance(1.1f);

        Assert.AreEqual(1, loaded);
        Assert.IsTrue(sut.IsReady);
    }
}
