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

        // NotDetermined는 "아직 안 물었다"와 "ATT가 없는 플랫폼"을 함께 뜻한다.
        // 제한으로 보면 안드로이드 광고가 통째로 비개인화로 떨어진다.
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
    public IEnumerator 추적을_거부하면_provider가_거부됨으로_받는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        var initializing = sut.InitializeAsync();

        tracking.Respond(TrackingAuthorizationStatus.Denied);

        await initializing;

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, provider.ReceivedContext.TrackingStatus);
        Assert.IsTrue(provider.ReceivedContext.IsTrackingRestricted,
                      "거부를 provider가 모르면 SDK는 동의받은 것으로 간주하고 계속 추적한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 추적을_허용하면_provider가_허용됨으로_받는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        var initializing = sut.InitializeAsync();

        tracking.Respond(TrackingAuthorizationStatus.Authorized);

        await initializing;

        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, provider.ReceivedContext.TrackingStatus);
        Assert.IsFalse(provider.ReceivedContext.IsTrackingRestricted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 권한을_묻지_않기로_했어도_현재_상태는_전달한다() => AwaitableTest.Run(async () =>
    {
        // 게임이 직접 묻는 모드(RequestTrackingAuthorization = false). AdService는 다이얼로그를
        // 띄우지 않지만, 게임이 이미 받아 둔 답이 있으면 그것을 provider에 실어야 한다.
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization
        {
            Status = TrackingAuthorizationStatus.Denied,
        };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(false),
                                new FakeRemovalStorage(), tracking);

        await sut.InitializeAsync();

        Assert.AreEqual(0, tracking.RequestCount, "묻지 않기로 했는데 다이얼로그를 띄웠다");
        Assert.IsTrue(provider.ReceivedContext.IsTrackingRestricted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 물을_수_없는_플랫폼은_제한으로_보지_않는다() => AwaitableTest.Run(async () =>
    {
        // 안드로이드·에디터 경로. UnsupportedTrackingAuthorization은 항상 NotDetermined다.
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), new UnsupportedTrackingAuthorization());

        await sut.InitializeAsync();

        Assert.IsFalse(provider.ReceivedContext.IsTrackingRestricted,
                       "ATT가 없는 플랫폼에서 추적을 끄면 이유 없이 수익이 죽는다");
    });
}
