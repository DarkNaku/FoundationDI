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

                return new AdService(provider, dispatcher, settings.ToOptions(), storage,
                                     ResolveConsent(container, dispatcher));
            }, Lifetime.Singleton, Resolution.Lazy);
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
