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
        builder.Register<IResourceProvider, AddressableResourceProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(ScriptableObject.CreateInstance<UIManagerSettings>());

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IUIManager>());
    }

    [Test]
    public void AddressableResourceProvider를_등록하면_IResourceService를_해석한다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceProvider, AddressableResourceProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IResourceService>());
    }

    [Test]
    public void ResourcesProvider를_등록하면_IResourceService를_해석한다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceProvider, ResourcesProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IResourceService>());
    }
}
