using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine;

public class ServiceResolverTest
{
    public interface IThing
    {
    }

    public sealed class Thing : IThing
    {
    }

    public sealed class Target
    {
        [Reflex.Attributes.Inject] public IThing Injected;
    }

    public sealed class TargetBehaviour : MonoBehaviour
    {
        [Reflex.Attributes.Inject] public IThing Injected;
    }

    private static Container BuildWithThing(IThing thing)
    {
        return new ContainerBuilder()
            .RegisterValue(thing, new[] { typeof(IThing) })
            .Build();
    }

    [Test]
    public void Resolve는_컨테이너의_인스턴스를_그대로_돌려준다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        var resolver = new ReflexServiceResolver(container);

        Assert.AreSame(thing, resolver.Resolve<IThing>());
    }

    [Test]
    public void TryResolve는_등록된_계약에_true와_인스턴스를_돌려준다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        var resolver = new ReflexServiceResolver(container);

        Assert.IsTrue(resolver.TryResolve<IThing>(out var resolved));
        Assert.AreSame(thing, resolved);
    }

    [Test]
    public void TryResolve는_미등록_계약에_false와_기본값을_돌려주고_예외를_던지지_않는다()
    {
        using var container = new ContainerBuilder().Build();

        var resolver = new ReflexServiceResolver(container);

        Assert.IsFalse(resolver.TryResolve<IThing>(out var resolved));
        Assert.IsNull(resolved);
    }

    [Test]
    public void Inject는_대상의_Inject_필드를_채운다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);
        var target = new Target();

        new ReflexServiceResolver(container).Inject(target);

        Assert.AreSame(thing, target.Injected);
    }

    [Test]
    public void InjectGameObject는_자손_컴포넌트까지_주입한다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        var root = new GameObject("root");
        var child = new GameObject("child");
        child.transform.SetParent(root.transform);
        var behaviour = child.AddComponent<TargetBehaviour>();

        try
        {
            new ReflexServiceResolver(container).InjectGameObject(root);

            Assert.AreSame(thing, behaviour.Injected);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void 인터페이스로만_다뤄도_동작한다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        IServiceResolver resolver = new ReflexServiceResolver(container);

        Assert.AreSame(thing, resolver.Resolve<IThing>());
    }

    [Test]
    public void 테스트에서_IServiceResolver를_대역으로_세울_수_있다()
    {
        // 기존 테스트 38곳이 Substitute.For<IObjectResolver>()에서 넘어올 자리다.
        // Reflex의 Container는 sealed라 이 대역이 불가능하다 - 그래서 seam이 필요하다.
        var resolver = Substitute.For<IServiceResolver>();
        var thing = new Thing();
        resolver.Resolve<IThing>().Returns(thing);

        Assert.AreSame(thing, resolver.Resolve<IThing>());
    }
}
