using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class AdServiceRegistration
    {
        // 루트 LifetimeScope의 Configure에서 호출한다.
        //   builder.RegisterAdService(_adServiceSettings);
        public static IContainerBuilder RegisterAdService(this IContainerBuilder builder,
                                                          AdServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AdService] AdServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterInstance(settings);
            builder.Register<IAdRemovalStorage, PlayerPrefsAdRemovalStorage>(Lifetime.Singleton);
            builder.Register<IAdDispatcher, UnityAdDispatcher>(Lifetime.Singleton);
            builder.Register<IAdProviderFactory, AdProviderFactory>(Lifetime.Singleton);

            builder.Register<IAdService>(container =>
            {
                var factory = container.Resolve<IAdProviderFactory>();
                var dispatcher = container.Resolve<IAdDispatcher>();
                var storage = container.Resolve<IAdRemovalStorage>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                return new AdService(provider, dispatcher, settings.ToOptions(), storage);
            }, Lifetime.Singleton);

            return builder;
        }
    }
}
