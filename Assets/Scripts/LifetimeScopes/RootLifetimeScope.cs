using DarkNaku.FoundationDI;
using FoundationDI.Host;
using VContainer;
using VContainer.Unity;

public class RootLifetimeScope : LifetimeScope
{
    // 인스펙터에서 Assets/Settings/UIManagerSettings.asset 을 연결한다.
    public UIManagerSettings settings;

    protected override void Configure(IContainerBuilder builder)
    {
        // 프리팹 로드는 Resources 백엔드 ResourceService에 위임한다.
        builder.Register<IResourceService, DefaultResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(settings);
        builder.RegisterHapticService();
        builder.RegisterEntryPoint<TestHubBootstrap>();
    }
}
