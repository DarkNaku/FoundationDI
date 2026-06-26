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
        builder.Register<IResourceService>(_ => new ResourceService(), Lifetime.Singleton);
        builder.RegisterUIManager(ScriptableObject.CreateInstance<UIManagerSettings>());

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IUIManager>());
    }
}
