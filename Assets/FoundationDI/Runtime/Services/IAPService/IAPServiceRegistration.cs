using System;
using Reflex.Core;
using Reflex.Enums;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class IAPServiceRegistration
    {
        // 테스트 seam. 에디트 모드에서는 Unity IAP가 스토어 팩토리를 등록하지 않아(런타임 초기화에서
        // 등록한다) StoreController를 만들 수 없다. 등록 테스트는 이걸 바꿔 배선만 검증한다.
        internal static Func<IIAPFulfillment, IReceiptValidator, IEntitlementStorage, bool, IIAPService> ServiceFactory =
            CreateDefault;

        // 루트 IInstaller의 InstallBindings에서 호출한다. 상품은 Unity IAP Catalog에서 읽는다.
        //   builder.RegisterIAPService();
        //
        // 지급 핸들러를 쓰려면 같은 InstallBindings 어디서든(순서 무관) 함께 등록한다.
        //   builder.RegisterType(typeof(MyFulfillment), new[] { typeof(IIAPFulfillment) },
        //                        Lifetime.Singleton, Resolution.Lazy);
        public static ContainerBuilder RegisterIAPService(this ContainerBuilder builder, bool verboseLogging = false)
        {
            return builder.RegisterFactory<IIAPService>(container =>
            {
                // 셋 다 선택 등록이다. 게임이 등록하지 않았으면 기본 구현으로 폴백하므로
                // 등록 순서에 의존하지 않는다.
                // Reflex에는 TryResolve가 없어 HasBinding으로 먼저 묻는다 -
                // 미등록 계약에 Resolve를 부르면 UnknownContractException이 난다.
                var fulfillment = container.HasBinding<IIAPFulfillment>()
                    ? container.Resolve<IIAPFulfillment>()
                    : new AutoConfirmFulfillment();

                var validator = container.HasBinding<IReceiptValidator>()
                    ? container.Resolve<IReceiptValidator>()
                    : new CrossPlatformReceiptValidator();

                var entitlements = container.HasBinding<IEntitlementStorage>()
                    ? container.Resolve<IEntitlementStorage>()
                    : new PlayerPrefsEntitlementStorage();

                return ServiceFactory(fulfillment, validator, entitlements, verboseLogging);
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        private static IIAPService CreateDefault(IIAPFulfillment fulfillment, IReceiptValidator validator,
                                                 IEntitlementStorage entitlements, bool verboseLogging) =>
            new UnityIAPService(fulfillment, validator, entitlements, verboseLogging);
    }
}
