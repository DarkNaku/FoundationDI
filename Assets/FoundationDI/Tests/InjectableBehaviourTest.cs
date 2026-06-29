using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class InjectableBehaviourTest
{
    private class TestInjectable : InjectableBehaviour
    {
        // EditMode에서는 AddComponent가 Awake를 자동 호출하지 않으므로 수동 트리거용.
        public void CallAwake() => Awake();
        public void CallEnsure() => EnsureInjected();
    }

    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("test");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    [Test]
    public void Awake에서_컴포넌트가_주입_요청된다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        new InjectorService(resolver).Start();   // 컨테이너 준비(즉시 주입 경로)

        var mb = _go.AddComponent<TestInjectable>();
        mb.CallAwake();                          // Awake → EnsureInjected → Request

        resolver.Received(1).Inject(mb);
    }

    [Test]
    public void EnsureInjected는_멱등하여_중복_요청하지_않는다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        new InjectorService(resolver).Start();
        var mb = _go.AddComponent<TestInjectable>();
        mb.CallAwake();                                // Awake에서 1회

        mb.CallEnsure();                               // 추가 호출은 무시되어야 함

        resolver.Received(1).Inject(mb);
    }
}
