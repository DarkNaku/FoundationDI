using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class IAPProviderFactoryTest
{
    [TearDown]
    public void TearDown() => IAPProviderRegistry.Reset();

    [Test]
    public void 강제_더미면_요청과_무관하게_Dummy이고_경고하지_않는다()
    {
        var resolved = IAPProviderFactory.Resolve(IAPProviderType.UnityIAP, forceDummy: true, out var warning);

        Assert.AreEqual(IAPProviderType.Dummy, resolved);
        Assert.IsNull(warning, "의도된 강제 더미인데 경고를 냈다");
    }

    [Test]
    public void 심볼이_없으면_경고와_함께_Dummy로_대체한다()
    {
        var resolved = IAPProviderFactory.Resolve(IAPProviderType.UnityIAP, forceDummy: false, out var warning);

#if FOUNDATIONDI_UNITYIAP
        Assert.AreEqual(IAPProviderType.UnityIAP, resolved);
        Assert.IsNull(warning);
#else
        Assert.AreEqual(IAPProviderType.Dummy, resolved);
        StringAssert.Contains("FOUNDATIONDI_UNITYIAP", warning);
#endif
    }

    [Test]
    public void Dummy_요청은_언제나_그대로_통과한다()
    {
        var resolved = IAPProviderFactory.Resolve(IAPProviderType.Dummy, forceDummy: false, out var warning);

        Assert.AreEqual(IAPProviderType.Dummy, resolved);
        Assert.IsNull(warning);
    }

    [Test]
    public void 등록된_creator가_있으면_그것으로_만든다()
    {
        var stub = new FakeIAPProvider();
        IAPProviderRegistry.Register(IAPProviderType.UnityIAP, _ => stub);

        var factory = new IAPProviderFactory();

        Assert.AreSame(stub, factory.Build(IAPProviderType.UnityIAP, DummyIAPOptions.Default));
    }

    [Test]
    public void Dummy는_레지스트리를_보지_않는다()
    {
        // 누가 Dummy에 creator를 등록해도 내장 구현이 이긴다.
        IAPProviderRegistry.Register(IAPProviderType.Dummy, _ => new FakeIAPProvider());

        var factory = new IAPProviderFactory();

        Assert.IsInstanceOf<DummyIAPProvider>(factory.Build(IAPProviderType.Dummy, DummyIAPOptions.Default));
    }

    [Test]
    public void 사용_가능하다고_판단됐는데_creator가_없으면_에러_후_Dummy다()
    {
        LogAssert.Expect(LogType.Error, new Regex("IAPService"));

        var factory = new IAPProviderFactory();

        Assert.IsInstanceOf<DummyIAPProvider>(factory.Build(IAPProviderType.UnityIAP, DummyIAPOptions.Default));
    }
}
