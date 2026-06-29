using NUnit.Framework;
using UnityEngine;
using VContainer;
using DarkNaku.FoundationDI;

public class DIRegistrationTests
{
    [Test]
    public void 컨테이너에서_IUIManager를_해석할_수_있다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(ScriptableObject.CreateInstance<UIManagerSettings>());

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IUIManager>());
    }

    [Test]
    public void AddressableResourceService로_등록하면_IResourceProvider_없이_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IResourceService>());
    }

    [Test]
    public void DefaultResourceService로_등록하면_IResourceProvider_없이_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceService, DefaultResourceService>(Lifetime.Singleton);

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IResourceService>());
    }
}
