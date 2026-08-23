using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class IapServiceRegistration
    {
        // 루트 LifetimeScope의 Configure에서 호출한다.
        //   builder.RegisterIapService(_iapServiceSettings);
        //
        // 지급 핸들러를 쓰려면 같은 Configure 어디서든(순서 무관) 함께 등록한다.
        //   builder.Register<IIapFulfillment, MyFulfillment>(Lifetime.Singleton);
        public static IContainerBuilder RegisterIapService(this IContainerBuilder builder,
                                                           IapServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[IAPService] IapServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterInstance(settings);
            builder.Register<IIapProviderFactory, IapProviderFactory>(Lifetime.Singleton);

            builder.Register<IIapService>(container =>
            {
                var factory = container.Resolve<IIapProviderFactory>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                // 셋 다 선택 등록이다. 게임이 등록하지 않았으면 기본 구현으로 폴백하므로
                // 등록 순서에 의존하지 않는다.
                var fulfillment = container.TryResolve<IIapFulfillment>(out var registeredFulfillment)
                    ? registeredFulfillment
                    : new AutoConfirmFulfillment();

                var validator = container.TryResolve<IReceiptValidator>(out var registeredValidator)
                    ? registeredValidator
                    : IapReceiptValidatorRegistry.ResolveOrDefault();

                var entitlements = container.TryResolve<IEntitlementStorage>(out var registeredStorage)
                    ? registeredStorage
                    : new PlayerPrefsEntitlementStorage();

                return new IapService(provider, settings.ToOptions(), fulfillment, validator, entitlements);
            }, Lifetime.Singleton);

            return builder;
        }
    }
}
