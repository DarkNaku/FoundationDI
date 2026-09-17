using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AdTrackingAuthorizationTest
{
    // 다이얼로그를 손으로 돌리는 가짜 구현. RequestAsync는 Respond를 부를 때까지 완료되지
    // 않으므로 "provider보다 먼저 물었는가"를 대기 상태로 관찰할 수 있다.
    private class FakeTrackingAuthorization : ITrackingAuthorization
    {
        private AwaitableCompletionSource<TrackingAuthorizationStatus> _pending;

        public bool IsSupported { get; set; } = true;
        public TrackingAuthorizationStatus Status { get; set; } = TrackingAuthorizationStatus.NotDetermined;
        public int RequestCount { get; private set; }
        public bool Throws { get; set; }

        public bool IsWaiting => _pending != null;

        public Awaitable<TrackingAuthorizationStatus> RequestAsync()
        {
            RequestCount++;

            if (Throws) throw new System.InvalidOperationException("요청이 이미 진행 중이다");

            _pending = new AwaitableCompletionSource<TrackingAuthorizationStatus>();
            return _pending.Awaitable;
        }

        public void Respond(TrackingAuthorizationStatus status)
        {
            var source = _pending;

            _pending = null;
            Status = status;
            source?.SetResult(status);
        }
    }

    private class FakeRemovalStorage : IAdRemovalStorage
    {
        public bool Load() => false;
        public void Save(bool removed) { }
    }

    private static AdServiceOptions NewOptions(bool requestTrackingAuthorization = true)
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
            requestTrackingAuthorization: requestTrackingAuthorization);
    }

    [TearDown]
    public void TearDown() => TrackingAuthorizationRegistry.Reset();

    [Test]
    public void 등록된_구현이_없으면_지원하지_않는_구현을_돌려준다()
    {
        var created = TrackingAuthorizationRegistry.Create(new FakeAdDispatcher());

        Assert.IsNotNull(created, "null을 돌려주면 호출부마다 널 검사를 해야 한다");
        Assert.IsFalse(created.IsSupported);
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, created.Status);
    }

    [Test]
    public void 등록된_구현이_있으면_그것을_돌려준다()
    {
        var registered = new FakeTrackingAuthorization();

        TrackingAuthorizationRegistry.Register(_ => registered);

        Assert.AreSame(registered, TrackingAuthorizationRegistry.Create(new FakeAdDispatcher()));
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지원하지_않는_구현은_즉시_완료된다() => AwaitableTest.Run(async () =>
    {
        var status = await new UnsupportedTrackingAuthorization().RequestAsync();

        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, status);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화는_provider보다_먼저_추적_권한을_요청한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        var initializing = sut.InitializeAsync();

        Assert.AreEqual(1, tracking.RequestCount, "권한을 묻지 않았다");
        Assert.IsTrue(tracking.IsWaiting, "권한 응답을 기다리지 않는다");
        Assert.IsFalse(sut.IsInitialized, "권한 응답 전에 초기화가 끝났다 — provider보다 먼저 물어야 한다");

        tracking.Respond(TrackingAuthorizationStatus.Authorized);

        Assert.IsTrue(await initializing);
        Assert.IsTrue(sut.IsInitialized);
        Assert.AreSame(tracking, sut.TrackingAuthorization);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_답을_받은_상태에서는_다시_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        var tracking = new FakeTrackingAuthorization { Status = TrackingAuthorizationStatus.Denied };
        var sut = new AdService(new FakeAdProvider(), new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(0, tracking.RequestCount, "거부된 상태인데 다이얼로그를 다시 띄우려 했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지원하지_않는_플랫폼에서는_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        var tracking = new FakeTrackingAuthorization { IsSupported = false };
        var sut = new AdService(new FakeAdProvider(), new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(0, tracking.RequestCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 설정이_꺼져_있으면_요청하지_않는다() => AwaitableTest.Run(async () =>
    {
        var tracking = new FakeTrackingAuthorization();
        var sut = new AdService(new FakeAdProvider(), new FakeAdDispatcher(),
                                NewOptions(requestTrackingAuthorization: false),
                                new FakeRemovalStorage(), tracking);

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(0, tracking.RequestCount, "설정을 껐는데도 다이얼로그를 띄웠다");
        Assert.AreSame(tracking, sut.TrackingAuthorization, "끈 상태에서도 게임이 직접 물을 수 있어야 한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 권한_요청이_실패해도_광고_초기화는_계속된다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization { Throws = true };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ATT 권한 요청"));

        Assert.IsTrue(await sut.InitializeAsync(), "권한 요청 예외가 초기화를 통째로 막았다");
        Assert.IsTrue(sut.IsInitialized);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 넘기지_않으면_지원하지_않는_구현으로_동작한다() => AwaitableTest.Run(async () =>
    {
        var sut = new AdService(new FakeAdProvider(), new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage());

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.IsFalse(sut.TrackingAuthorization.IsSupported);
    });
}
