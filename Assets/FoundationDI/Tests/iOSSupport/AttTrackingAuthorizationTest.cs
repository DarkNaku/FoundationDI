using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
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

    [UnityTest]
    public IEnumerator 지원하지_않으면_묻지_않고_현재_상태로_즉시_답한다() => AwaitableTest.Run(async () =>
    {
        var binding = new FakeAttBinding { IsSupported = false };
        var sut = new AttTrackingAuthorization(new FakeAdDispatcher(), binding);

        var request = sut.RequestAsync();

        Assert.IsTrue(request.GetAwaiter().IsCompleted);
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, await request);
        Assert.AreEqual(0, binding.RequestCount);
    });

    [UnityTest]
    public IEnumerator 진행_중에_다시_부르면_현재_상태로_즉시_답한다() => AwaitableTest.Run(async () =>
    {
        var binding = new FakeAttBinding();
        var sut = new AttTrackingAuthorization(new FakeAdDispatcher(), binding);

        var first = sut.RequestAsync();
        var second = sut.RequestAsync();

        Assert.IsFalse(first.GetAwaiter().IsCompleted, "첫 요청은 사용자의 답을 기다려야 한다");
        Assert.IsTrue(second.GetAwaiter().IsCompleted, "진행 중인 요청에 편승시키면 안 된다");
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, await second);
    });
}
