using UnityEngine;
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private UIManagerSettings _uiSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IResourceService>(_ => new ResourceService(), Lifetime.Singleton);
        builder.RegisterUIManager(_uiSettings);
    }
}
