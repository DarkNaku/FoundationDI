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

            return builder.RegisterFactory<IAnalyticsService>(container =>
            {
                var factory = container.Resolve<IAnalyticsProviderFactory>();
                var options = settings.ToOptions();
                var types = settings.ResolveProviders(Application.isEditor);
                var providers = factory.CreateAll(types, options, settings.ProviderSettings);

                return new AnalyticsService(providers, options);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
