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
        // 백엔드 교체는 이 provider 등록 한 줄만 바꾼다 (예: AddressableResourceProvider).
        builder.Register<IResourceProvider, ResourcesProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(settings);
        builder.RegisterHapticService();
        builder.RegisterInitializeService();
        builder.RegisterEntryPoint<TestHubBootstrap>();
    }
}
