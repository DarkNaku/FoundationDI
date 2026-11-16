using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class AdServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterAdService(_adServiceSettings);
        public static ContainerBuilder RegisterAdService(this ContainerBuilder builder,
                                                         AdServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AdService] AdServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(PlayerPrefsAdRemovalStorage), new[] { typeof(IAdRemovalStorage) },
                                 Lifetime.Singleton, Resolution.Lazy);
            builder.RegisterMainThreadDispatcher();
            builder.RegisterType(typeof(AdProviderFactory), new[] { typeof(IAdProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IAdService>(container =>
            {
                var factory = container.Resolve<IAdProviderFactory>();
                var dispatcher = container.Resolve<IAdDispatcher>();
                var storage = container.Resolve<IAdRemovalStorage>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                var service = new AdService(provider, dispatcher, settings.ToOptions(), storage,
                                            ResolveConsent(container, dispatcher));

                if (settings.ReportStateToAnalytics) AttachStateReporter(container, service);

                return service;
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        // AdService를 해석하는 시점에 붙인다 — 그때 ad_state를 현재 상태로 덮어 지난 세션 값이 남지 않는다.
        // 분석 서비스는 초기화 전 이벤트를 버퍼하고 사용자 속성을 이벤트보다 먼저 내보내므로, 그 전에 남긴
        // 이벤트에도 이번 값이 붙는다. 리포터는 서비스 이벤트에 붙어 서비스와 수명을 같이 한다.
        private static void AttachStateReporter(Container container, IAdService service)
        {
            if (!container.HasBinding<IAnalyticsService>())
            {
                Debug.LogWarning("[AdService] Report State To Analytics가 켜져 있지만 IAnalyticsService가 등록되지 않았다. " +
                                 "ad_state를 남기지 않는다.");
                return;
            }

            _ = new AdStateReporter(service, container.Resolve<IAnalyticsService>());
        }

        // ConsentService를 등록하지 않은 프로젝트도 0.11처럼 ATT는 묻게 한다. 다만 약관·UMP 없이
        // EEA에 Google 수요를 내보내면 제한 광고만 나가므로 경고를 남긴다.
        private static IConsentService ResolveConsent(Container container, IAdDispatcher dispatcher)
        {
            if (container.HasBinding<IConsentService>()) return container.Resolve<IConsentService>();

            Debug.LogWarning("[AdService] IConsentService가 등록되지 않았다. 약관·UMP 없이 ATT만 묻는다. " +
                             "EEA·영국에서 광고를 내보내려면 RegisterConsentService로 UMP를 켠다.");

            return ConsentService.CreateTrackingOnly(dispatcher, TrackingAuthorizationRegistry.Create(dispatcher));
        }
    }
}
