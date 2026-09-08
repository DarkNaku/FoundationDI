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
            builder.RegisterType(typeof(UnityAdDispatcher), new[] { typeof(IAdDispatcher) },
                                 Lifetime.Singleton, Resolution.Lazy);
            builder.RegisterType(typeof(AdProviderFactory), new[] { typeof(IAdProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IAdService>(container =>
            {
                var factory = container.Resolve<IAdProviderFactory>();
                var dispatcher = container.Resolve<IAdDispatcher>();
                var storage = container.Resolve<IAdRemovalStorage>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                return new AdService(provider, dispatcher, settings.ToOptions(), storage);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
