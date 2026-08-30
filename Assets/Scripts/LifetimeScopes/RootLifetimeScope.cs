using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class RootLifetimeScope : LifetimeScope
{
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

        builder.RegisterHapticService();
        builder.RegisterInitializeService();
        builder.RegisterAdService(_adServiceSettings);
        builder.RegisterAnalyticsService(_analyticsServiceSettings);
        builder.RegisterIapService(_iapServiceSettings);

        // TutorialManager는 원래 씬 LifetimeScope에 등록하는 게 기본이다.
        // 여기 남기는 이유: InjectorService는 정적 리졸버 하나를 공유해 씬 배치 컴포넌트를 항상 루트로 주입하므로, RegisterInjector와 다른 스코프로 갈리면 그 주입이 조용히 실패한다.
        builder.RegisterTutorialManager();
    }
}
