using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;

public class ServiceResolverBootstrapTests
{
    public interface IScopedThing
    {
    }

    public sealed class ScopedThing : IScopedThing
    {
    }

    [Test]
    public void 부트스트랩을_건_컨테이너는_IServiceResolver를_해석한다()
    {
        var builder = new ContainerBuilder();
        ServiceResolverBootstrap.Register(builder);

        using var container = builder.Build();

        Assert.IsNotNull(container.Resolve<IServiceResolver>());
    }

    [Test]
    public void 해석된_리졸버는_자기_컨테이너를_감싼다()
    {
        var thing = new ScopedThing();
        var builder = new ContainerBuilder();
        ServiceResolverBootstrap.Register(builder);
        builder.RegisterValue(thing, new[] { typeof(IScopedThing) });

        using var container = builder.Build();
        var resolver = container.Resolve<IServiceResolver>();

        // 부모 컨테이너의 리졸버를 물려받으면 이 바인딩을 못 본다.
        // 씬 스코프에만 등록된 서비스(UINavigator 등)를 해석하지 못하게 되는 지점이다.
        Assert.IsTrue(resolver.TryResolve<IScopedThing>(out var resolved));
        Assert.AreSame(thing, resolved);
    }

    [Test]
    public void 같은_컨테이너에서_두_번_해석해도_같은_인스턴스다()
    {
        var builder = new ContainerBuilder();
        ServiceResolverBootstrap.Register(builder);

        using var container = builder.Build();

        Assert.AreSame(container.Resolve<IServiceResolver>(), container.Resolve<IServiceResolver>());
    }
}
