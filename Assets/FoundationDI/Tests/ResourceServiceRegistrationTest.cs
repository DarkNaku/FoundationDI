using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;

public class ResourceServiceRegistrationTest
{
    [Test]
    public void Resources_백엔드로_등록하면_ResourceService와_ResourcesProvider로_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterResourceService();

        using var container = builder.Build();

        Assert.IsInstanceOf<ResourceService>(container.Resolve<IResourceService>());
        Assert.IsInstanceOf<ResourcesProvider>(container.Resolve<IResourceProvider>());
    }

    [Test]
    public void Addressables_백엔드로_등록하면_AddressablesProvider로_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterResourceService(ResourceBackend.Addressables);

        using var container = builder.Build();

        Assert.IsInstanceOf<AddressablesProvider>(container.Resolve<IResourceProvider>());
    }

    [Test]
    public void 두번_해석하면_같은_인스턴스를_돌려준다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterResourceService();

        using var container = builder.Build();

        Assert.AreSame(container.Resolve<IResourceService>(), container.Resolve<IResourceService>());
        Assert.AreSame(container.Resolve<IResourceProvider>(), container.Resolve<IResourceProvider>());
    }
}
