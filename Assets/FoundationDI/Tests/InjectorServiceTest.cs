using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class InjectorServiceTest
{
    private class DummyBehaviour : MonoBehaviour { }

    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        // 정적 상태 초기화(이전 테스트 잔재 제거)
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("dummy");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    [Test]
    public void 컨테이너_준비전_Request는_보류되어_즉시_주입되지_않는다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var mb = _go.AddComponent<DummyBehaviour>();

        InjectorService.Request(mb);

        resolver.DidNotReceive().Inject(mb);
    }

    [Test]
    public void Start로_컨테이너가_바인딩되면_보류분이_주입된다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var mb = _go.AddComponent<DummyBehaviour>();
        InjectorService.Request(mb);          // 보류 (아직 바인딩 전)

        new InjectorService(resolver).Start(); // 바인딩 + flush

        resolver.Received(1).Inject(mb);
    }

    [Test]
    public void 컨테이너_준비후_Request는_즉시_주입한다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        new InjectorService(resolver).Start(); // 먼저 바인딩
        var mb = _go.AddComponent<DummyBehaviour>();

        InjectorService.Request(mb);

        resolver.Received(1).Inject(mb);
    }

    [Test]
    public void Dispose는_정적상태를_초기화한다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var sut = new InjectorService(resolver);
        sut.Start();
        sut.Dispose();                         // 바인딩 해제
        var mb = _go.AddComponent<DummyBehaviour>();

        InjectorService.Request(mb);           // 다시 보류로 동작해야 함

        resolver.DidNotReceive().Inject(mb);
    }

    [Test]
    public void Request_null은_예외없이_무시한다()
    {
        Assert.DoesNotThrow(() => InjectorService.Request(null));
    }
}
