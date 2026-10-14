using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class AnalyticsServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterAnalyticsService(_analyticsServiceSettings);
        public static ContainerBuilder RegisterAnalyticsService(this ContainerBuilder builder,
                                                                AnalyticsServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AnalyticsService] AnalyticsServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(AnalyticsProviderFactory), new[] { typeof(IAnalyticsProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            builder.RegisterFactory<AnalyticsProviderInitializer>(container =>
            {
                var factory = container.Resolve<IAnalyticsProviderFactory>();
                var options = settings.ToOptions();
                var types = settings.ResolveProviders(Application.isEditor);
                var providers = factory.CreateAll(types, options, settings.ProviderSettings);

                return new AnalyticsProviderInitializer(providers);
            }, new[] { typeof(AnalyticsProviderInitializer), typeof(IConsentPrerequisite) },
               Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IAnalyticsService>(container =>
            {
                var initializer = container.Resolve<AnalyticsProviderInitializer>();

                return new AnalyticsService(initializer, settings.ToOptions(), ResolveConsent(container, settings));
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        private static IConsentService ResolveConsent(Container container, AnalyticsServiceSettings settings)
        {
            if (!settings.WaitForConsent) return null;

            if (container.HasBinding<IConsentService>()) return container.Resolve<IConsentService>();

            Debug.LogWarning("[AnalyticsService] Wait For Consent가 켜져 있지만 IConsentService가 등록되지 않았다. " +
                             "동의를 기다리지 않고 수집한다.");
            return null;
        }
    }
}
