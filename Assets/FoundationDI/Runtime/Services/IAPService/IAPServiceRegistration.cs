using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class IAPServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterIAPService(_iapServiceSettings);
        //
        // 지급 핸들러를 쓰려면 같은 InstallBindings 어디서든(순서 무관) 함께 등록한다.
        //   builder.RegisterType(typeof(MyFulfillment), new[] { typeof(IIAPFulfillment) },
        //                        Lifetime.Singleton, Resolution.Lazy);
        public static ContainerBuilder RegisterIAPService(this ContainerBuilder builder,
                                                          IAPServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[IAPService] IAPServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(IAPProviderFactory), new[] { typeof(IIAPProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IIAPService>(container =>
            {
                var factory = container.Resolve<IIAPProviderFactory>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                // 셋 다 선택 등록이다. 게임이 등록하지 않았으면 기본 구현으로 폴백하므로
                // 등록 순서에 의존하지 않는다.
                // Reflex에는 TryResolve가 없어 HasBinding으로 먼저 묻는다 -
                // 미등록 계약에 Resolve를 부르면 UnknownContractException이 난다.
                var fulfillment = container.HasBinding<IIAPFulfillment>()
                    ? container.Resolve<IIAPFulfillment>()
                    : new AutoConfirmFulfillment();

                var validator = container.HasBinding<IReceiptValidator>()
                    ? container.Resolve<IReceiptValidator>()
                    : IAPReceiptValidatorRegistry.ResolveOrDefault();

                var entitlements = container.HasBinding<IEntitlementStorage>()
                    ? container.Resolve<IEntitlementStorage>()
                    : new PlayerPrefsEntitlementStorage();

                return new IAPService(provider, settings.ToOptions(), fulfillment, validator, entitlements);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
