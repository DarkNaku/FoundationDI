using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AttTrackingAuthorizationTest
{
    private class FakeAttBinding : IAttBinding
    {
        public bool IsSupported { get; set; } = true;
        public TrackingAuthorizationStatus Status { get; set; } = TrackingAuthorizationStatus.NotDetermined;
        public int RequestCount { get; private set; }

        public void Request() => RequestCount++;
    }

    private class FakeAppActivity : IAppActivity
    {
        public bool IsActive { get; set; }
        public int ResignActiveCount { get; private set; }

        // 시스템 팝업이 뜬 순간. 앱이 Inactive로 내려가며 카운트가 오른다.
        public void ResignActive()
        {
            IsActive = false;
            ResignActiveCount++;
        }
    }

    // 정확히 표현되는 단위로 시간을 흘려 부동소수 누적 오차를 피한다.
    private const float Step = 0.25f;

    private FakeAdDispatcher _dispatcher;
    private FakeAttBinding _binding;
    private FakeAppActivity _activity;
    private AttTrackingAuthorization _sut;

    [SetUp]
    public void SetUp()
    {
        _dispatcher = new FakeAdDispatcher();
        _binding = new FakeAttBinding();
        _activity = new FakeAppActivity();
        _sut = new AttTrackingAuthorization(_dispatcher, _binding, _activity);
    }

    private void Run(float seconds)
    {
        for (var elapsed = 0f; elapsed < seconds; elapsed += Step)
        {
            _dispatcher.TickFrames(1);
            _dispatcher.Advance(Step);
        }
    }

    // 다음 요청이 나갈 때까지 걸린 시간. maxSeconds 안에 나가지 않으면 -1.
    private float RunUntilNextRequest(float maxSeconds)
    {
        var before = _binding.RequestCount;

        for (var elapsed = Step; elapsed <= maxSeconds; elapsed += Step)
        {
            _dispatcher.TickFrames(1);
            _dispatcher.Advance(Step);

            if (_binding.RequestCount != before) return elapsed;
        }

        return -1f;
    }

    private static bool IsDone(Awaitable<TrackingAuthorizationStatus> request) =>
        request.GetAwaiter().IsCompleted;

    [UnityTest]
    public IEnumerator 지원하지_않으면_묻지_않고_현재_상태로_즉시_답한다() => AwaitableTest.Run(async () =>
    {
        _binding.IsSupported = false;

        var request = _sut.RequestAsync();

        Assert.IsTrue(IsDone(request));
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, await request);
        Assert.AreEqual(0, _binding.RequestCount);
    });

    [UnityTest]
    public IEnumerator 진행_중에_다시_부르면_현재_상태로_즉시_답한다() => AwaitableTest.Run(async () =>
    {
        var first = _sut.RequestAsync();
        var second = _sut.RequestAsync();

        Assert.IsFalse(IsDone(first), "첫 요청은 사용자의 답을 기다려야 한다");
        Assert.IsTrue(IsDone(second), "진행 중인 요청에 편승시키면 안 된다");
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, await second);
    });

    [Test]
    public void Inactive_동안에는_요청하지_않는다()
    {
        _activity.IsActive = false;

        _sut.RequestAsync();
        Run(10f);

        Assert.AreEqual(0, _binding.RequestCount,
                        "Inactive에서 보낸 요청은 iOS가 팝업 없이 버린다 — 첫 씬 Awake가 그 구간이다");
    }

    [Test]
    public void Active가_되고_안정화_대기를_거친_뒤에_요청한다()
    {
        _activity.IsActive = false;

        _sut.RequestAsync();
        Run(1f);

        _activity.IsActive = true;
        _dispatcher.TickFrames(1);

        _dispatcher.Advance(AttTrackingAuthorization.SettleSeconds / 2f);
        Assert.AreEqual(0, _binding.RequestCount, "Active가 되자마자 요청했다 — 안정화 대기가 없다");

        _dispatcher.Advance(AttTrackingAuthorization.SettleSeconds / 2f);
        Assert.AreEqual(1, _binding.RequestCount);
    }

    [Test]
    public void 안정화_대기_중_다시_Inactive가_되면_요청하지_않고_Active를_다시_기다린다()
    {
        _activity.IsActive = true;

        _sut.RequestAsync();

        _activity.IsActive = false;
        _dispatcher.Advance(AttTrackingAuthorization.SettleSeconds);
        Run(5f);

        Assert.AreEqual(0, _binding.RequestCount);

        _activity.IsActive = true;

        Assert.AreNotEqual(-1f, RunUntilNextRequest(5f), "Active로 돌아왔는데 요청하지 않는다");
    }

    [UnityTest]
    public IEnumerator 이미_확정된_상태면_Active를_기다리지_않고_완료한다() => AwaitableTest.Run(async () =>
    {
        _binding.Status = TrackingAuthorizationStatus.Denied;
        _activity.IsActive = false;

        var request = _sut.RequestAsync();

        Assert.IsTrue(IsDone(request));
        Assert.AreEqual(TrackingAuthorizationStatus.Denied, await request);
        Assert.AreEqual(0, _binding.RequestCount);
    });

    [UnityTest]
    public IEnumerator 확정_상태가_되면_즉시_완료하고_더_요청하지_않는다() => AwaitableTest.Run(async () =>
    {
        _activity.IsActive = true;

        var request = _sut.RequestAsync();

        _dispatcher.Advance(AttTrackingAuthorization.SettleSeconds);
        Assert.AreEqual(1, _binding.RequestCount);

        _activity.ResignActive();
        _dispatcher.TickFrames(1);

        _binding.Status = TrackingAuthorizationStatus.Authorized;
        _activity.IsActive = true;
        _dispatcher.TickFrames(1);

        Assert.IsTrue(IsDone(request), "확정됐는데 완료하지 않는다");
        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, await request);

        Run(100f);

        Assert.AreEqual(1, _binding.RequestCount, "이미 답을 받았는데 다시 요청했다");
        Assert.AreEqual(0, _dispatcher.PendingCount, "완료 후에도 예약이 남아 있다");
    });

    [Test]
    public void 팝업_없이_NotDetermined가_유지되면_점증_간격으로_재요청한다()
    {
        _activity.IsActive = true;

        _sut.RequestAsync();

        var gaps = new List<float> { RunUntilNextRequest(60f) };

        for (var i = 0; i < AttTrackingAuthorization.MaxRetries; i++) gaps.Add(RunUntilNextRequest(60f));

        Assert.AreEqual(1 + AttTrackingAuthorization.MaxRetries, _binding.RequestCount);

        for (var i = 1; i < gaps.Count; i++)
        {
            Assert.AreNotEqual(-1f, gaps[i], $"{i + 1}번째 요청이 나가지 않았다");
            Assert.AreEqual(AttTrackingAuthorization.IgnoreWindowSeconds +
                            AttTrackingAuthorization.RetryDelaySeconds[i - 1] +
                            AttTrackingAuthorization.SettleSeconds, gaps[i], 0.001f,
                            $"{i + 1}번째 요청 간격");
        }

        for (var i = 2; i < gaps.Count; i++) Assert.Greater(gaps[i], gaps[i - 1], "간격이 점증하지 않는다");
    }

    [UnityTest]
    public IEnumerator 재요청은_상한까지만_하고_그_뒤_무시되면_현재_상태로_완료한다() => AwaitableTest.Run(async () =>
    {
        _activity.IsActive = true;

        var request = _sut.RequestAsync();

        for (var i = 0; i <= AttTrackingAuthorization.MaxRetries; i++) RunUntilNextRequest(60f);

        Run(AttTrackingAuthorization.IgnoreWindowSeconds);

        Assert.IsTrue(IsDone(request), "상한을 넘겼는데 타임아웃까지 붙잡고 있다 — 광고 초기화가 그만큼 늦는다");
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, await request);

        Run(100f);

        Assert.AreEqual(1 + AttTrackingAuthorization.MaxRetries, _binding.RequestCount);
    });

    [Test]
    public void Inactive를_거쳐_Active로_돌아왔는데도_NotDetermined면_재요청한다()
    {
        _activity.IsActive = true;

        _sut.RequestAsync();
        _dispatcher.Advance(AttTrackingAuthorization.SettleSeconds);

        // 다른 권한 팝업(알림 등)이 같이 떠서 ATT가 버려진 경우.
        _activity.ResignActive();
        Run(1f);
        _activity.IsActive = true;

        Assert.AreNotEqual(-1f, RunUntilNextRequest(30f), "팝업을 거쳐 돌아왔는데 답이 없는데도 재요청하지 않는다");
        Assert.AreEqual(2, _binding.RequestCount);
    }

    [UnityTest]
    public IEnumerator 팝업이_떠_있는_동안에는_무시로_판정하지_않는다() => AwaitableTest.Run(async () =>
    {
        _activity.IsActive = true;

        var request = _sut.RequestAsync();
        _dispatcher.Advance(AttTrackingAuthorization.SettleSeconds);

        _activity.ResignActive();
        Run(30f);

        Assert.AreEqual(1, _binding.RequestCount, "사용자가 팝업을 보고 있는데 다시 요청했다");
        Assert.IsFalse(IsDone(request));

        _binding.Status = TrackingAuthorizationStatus.Denied;
        _activity.IsActive = true;
        _dispatcher.TickFrames(1);

        Assert.IsTrue(IsDone(request));
        Assert.AreEqual(TrackingAuthorizationStatus.Denied, await request);
        Assert.AreEqual(1, _binding.RequestCount);
    });

    [UnityTest]
    public IEnumerator 전체_타임아웃이_지나면_현재_상태로_완료한다() => AwaitableTest.Run(async () =>
    {
        _activity.IsActive = false;

        var request = _sut.RequestAsync();

        Run(AttTrackingAuthorization.TimeoutSeconds - 1f);
        Assert.IsFalse(IsDone(request));

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ATT"));
        Run(1f);

        Assert.IsTrue(IsDone(request), "Active가 끝내 오지 않으면 InitializeAsync가 영원히 멈춘다");
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, await request);
        Assert.AreEqual(0, _binding.RequestCount);
    });
}
