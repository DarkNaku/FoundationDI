using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TrackingAuthorizationRegistryTest
{
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
        var registered = new UnsupportedTrackingAuthorization();

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
}
