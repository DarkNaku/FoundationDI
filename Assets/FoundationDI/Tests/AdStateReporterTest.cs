using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;

public class AdStateReporterTest
{
    [Test]
    public void 만들자마자_ad_state를_현재_상태로_설정한다()
    {
        // 사용자 속성은 앱을 다시 켜도 남는다. 덮지 않으면 이번 세션 이벤트에 지난 세션의 ready가 붙는다.
        var ads = Substitute.For<IAdService>();
        ads.InitState.Returns(AdInitState.Pending);
        var analytics = Substitute.For<IAnalyticsService>();

        using var sut = new AdStateReporter(ads, analytics);

        analytics.Received(1).SetUserProperty("ad_state", "pending");
    }

    [Test]
    public void 상태가_바뀔_때마다_ad_state를_갱신한다()
    {
        var ads = Substitute.For<IAdService>();
        var analytics = Substitute.For<IAnalyticsService>();
        using var sut = new AdStateReporter(ads, analytics);

        ads.InitStateChanged += Raise.Event<System.Action<AdInitState>>(AdInitState.WaitingConsent);
        ads.InitStateChanged += Raise.Event<System.Action<AdInitState>>(AdInitState.Ready);
        ads.InitStateChanged += Raise.Event<System.Action<AdInitState>>(AdInitState.ReadyRestricted);
        ads.InitStateChanged += Raise.Event<System.Action<AdInitState>>(AdInitState.Failed);

        Received.InOrder(() =>
        {
            analytics.SetUserProperty("ad_state", "pending");
            analytics.SetUserProperty("ad_state", "waiting_consent");
            analytics.SetUserProperty("ad_state", "ready");
            analytics.SetUserProperty("ad_state", "ready_restricted");
            analytics.SetUserProperty("ad_state", "failed");
        });
    }

    [Test]
    public void 해제하면_더_이상_갱신하지_않는다()
    {
        var ads = Substitute.For<IAdService>();
        var analytics = Substitute.For<IAnalyticsService>();
        var sut = new AdStateReporter(ads, analytics);

        sut.Dispose();
        ads.InitStateChanged += Raise.Event<System.Action<AdInitState>>(AdInitState.Ready);

        analytics.DidNotReceive().SetUserProperty("ad_state", "ready");
    }
}
