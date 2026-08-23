using DarkNaku.FoundationDI;
using FoundationDI.Host;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class RootLifetimeScope : LifetimeScope
{
    // 인스펙터에서 Assets/Settings/UIServiceSettings.asset 을 연결한다.
    public UIServiceSettings settings;

    // 인스펙터에서 Assets/Settings/AdServiceSettings.asset 을 연결한다.
    [SerializeField] private AdServiceSettings _adServiceSettings;

    // 인스펙터에서 Assets/Settings/AnalyticsServiceSettings.asset 을 연결한다.
    [SerializeField] private AnalyticsServiceSettings _analyticsServiceSettings;

    // 인스펙터에서 Assets/Settings/IapServiceSettings.asset 을 연결한다.
    [SerializeField] private IapServiceSettings _iapServiceSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        // 프리팹 로드는 Resources 백엔드 ResourceService에 위임한다.
        // 백엔드 교체는 이 provider 등록 한 줄만 바꾼다 (예: AddressablesProvider).
        builder.Register<IResourceProvider, ResourcesProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterMessageService();

        // 씬에 직접 배치된 컴포넌트(TutorialSequenceBehaviour/TutorialTarget 등)의 주입 경로.
        builder.RegisterInjector();

        builder.RegisterUIService(settings);
        builder.RegisterHapticService();
        builder.RegisterInitializeService();
        builder.RegisterAdService(_adServiceSettings);
        builder.RegisterAnalyticsService(_analyticsServiceSettings);
        builder.RegisterIapService(_iapServiceSettings);

        // TutorialManager는 원래 씬 LifetimeScope에 등록하는 게 기본이다.
        // 이 호스트 프로젝트는 루트 스코프 하나뿐이라 여기에 붙인다(전역 수명이 된다).
        builder.RegisterTutorialManager();

        builder.RegisterEntryPoint<TestHubBootstrap>();
    }
}
