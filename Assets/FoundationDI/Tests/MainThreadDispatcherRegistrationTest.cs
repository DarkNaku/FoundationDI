using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;

public class MainThreadDispatcherRegistrationTest
{
    [Test]
    public void 등록하면_광고용과_공용_계약이_같은_바인딩을_가리킨다()
    {
        var builder = new ContainerBuilder();

        builder.RegisterMainThreadDispatcher();

        Assert.IsTrue(builder.HasBinding(typeof(IMainThreadDispatcher)));
        Assert.IsTrue(builder.HasBinding(typeof(IAdDispatcher)));
    }

    [Test]
    public void 두_번_불러도_바인딩을_하나만_만든다()
    {
        var builder = new ContainerBuilder();

        builder.RegisterMainThreadDispatcher();
        builder.RegisterMainThreadDispatcher();

        // 두 번 등록되면 서비스마다 러너 GameObject가 따로 생겨 펌프가 두 벌이 된다.
        Assert.AreEqual(1, builder.Bindings.Count);
    }
}
