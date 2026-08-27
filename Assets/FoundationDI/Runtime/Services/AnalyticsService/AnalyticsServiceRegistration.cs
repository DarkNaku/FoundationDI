using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class AnalyticsServiceRegistration
    {
        // 루트 LifetimeScope의 Configure에서 호출한다.
        //   builder.RegisterAnalyticsService(_analyticsServiceSettings);
        public static IContainerBuilder RegisterAnalyticsService(this IContainerBuilder builder,
                                                                 AnalyticsServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AnalyticsService] AnalyticsServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterInstance(settings);
            builder.Register<IAnalyticsProviderFactory, AnalyticsProviderFactory>(Lifetime.Singleton);

            builder.Register<IAnalyticsService>(container =>
            {
                var factory = container.Resolve<IAnalyticsProviderFactory>();
                var options = settings.ToOptions();
                var types = settings.ResolveProviders(Application.isEditor);
                var providers = factory.CreateAll(types, options, settings.ProviderSettings);

                return new AnalyticsService(providers, options);
            }, Lifetime.Singleton);

            return builder;
        }
    }
}
