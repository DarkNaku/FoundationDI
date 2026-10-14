using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ConsentServiceTrackingTest
{
    private static ConsentFixture WithTracking()
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;
        f.Tracking.IsSupported = true;
        return f;
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_다음에_추적_권한을_묻고_응답을_결과에_싣는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        var requesting = f.Build().RequestAsync();

        Assert.IsTrue(f.Tracking.IsWaiting, "추적 권한을 묻지 않았다");
        CollectionAssert.AreEqual(new[] { "ump.update", "ump.form", "att" }, f.Log);

        f.Tracking.Respond(TrackingAuthorizationStatus.Denied);
        var result = await requesting;

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, result.TrackingStatus);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_답한_상태면_다시_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        // UMP의 IDFA 설명 메시지가 ATT를 먼저 띄운 경우도 이 경로다.
        var f = WithTracking();
        f.Tracking.Status = TrackingAuthorizationStatus.Authorized;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Tracking.RequestCount);
        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, result.TrackingStatus);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지원하지_않는_플랫폼에서는_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.Tracking.IsSupported = false;

        await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Tracking.RequestCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 설정이_꺼져_있으면_묻지_않지만_게임이_직접_물을_수_있다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.RequestTracking = false;
        var sut = f.Build();

        await sut.RequestAsync();

        Assert.AreEqual(0, f.Tracking.RequestCount, "설정을 껐는데도 다이얼로그를 띄웠다");
        Assert.AreSame(f.Tracking, sut.TrackingAuthorization);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 묻지_않기로_했어도_현재_상태는_결과에_싣는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.RequestTracking = false;
        f.Tracking.Status = TrackingAuthorizationStatus.Denied;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, result.TrackingStatus);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 권한_요청이_예외를_던져도_완료된다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.Tracking.Throws = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ATT 권한 요청"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 다시_요청해도_추적_권한은_세션에_한_번만_묻는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.Ump.CanRequestAds = false;   // 확정되지 않아 재요청이 다시 돈다
        var sut = f.Build();

        var first = sut.RequestAsync();
        f.Tracking.Respond(TrackingAuthorizationStatus.NotDetermined);   // 무시된 경우를 흉내
        await first;
        await sut.RequestAsync();

        Assert.AreEqual(1, f.Tracking.RequestCount);
    });
}
