using DarkNaku.FoundationDI;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

/// <summary>
/// 앱 수명 서비스를 등록한다. ContainerScope와 함께 루트 스코프 프리팹에 붙이고
/// Resources/ReflexSettings.asset 의 RootScopes 목록에 넣는다.
/// </summary>
public class RootInstaller : MonoBehaviour, IInstaller
{
    // 인스펙터에서 Assets/Settings/AdServiceSettings.asset 을 연결한다.
    [SerializeField] private AdServiceSettings _adServiceSettings;

    // 인스펙터에서 Assets/Settings/AnalyticsServiceSettings.asset 을 연결한다.
    [SerializeField] private AnalyticsServiceSettings _analyticsServiceSettings;

    // 인스펙터에서 Assets/Settings/IapServiceSettings.asset 을 연결한다.
    [SerializeField] private IapServiceSettings _iapServiceSettings;

    public void InstallBindings(ContainerBuilder builder)
    {
        // 프리팹 로드는 Resources 백엔드 ResourceService에 위임한다.
        // 백엔드 교체는 이 provider 등록 한 줄만 바꾼다 (예: AddressablesProvider).
        builder.RegisterType(typeof(ResourcesProvider), new[] { typeof(IResourceProvider) },
                             Lifetime.Singleton, Resolution.Lazy);
        builder.RegisterType(typeof(ResourceService), new[] { typeof(IResourceService) },
                             Lifetime.Singleton, Resolution.Lazy);

        builder.RegisterMessageService();

        // builder.RegisterInjector()는 사라졌다 - 씬에 배치된 컴포넌트의 주입은
        // ContainerScope가 어떤 Awake보다도 먼저 직접 수행한다.

        builder.RegisterHapticService();
        builder.RegisterInitializeService();
        builder.RegisterAdService(_adServiceSettings);
        builder.RegisterAnalyticsService(_analyticsServiceSettings);
        builder.RegisterIapService(_iapServiceSettings);

        // RegisterTutorialManager는 SceneInstaller로 내려갔다.
        // 루트에 붙들어 두던 이유(InjectorService가 정적 리졸버 하나를 공유한다)가 사라졌다.
    }
}
