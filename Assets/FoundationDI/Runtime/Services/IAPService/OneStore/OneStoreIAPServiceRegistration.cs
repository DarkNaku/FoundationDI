using System;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class OneStoreIAPServiceRegistration
    {
        // 테스트 seam. 에디트 모드에는 원스토어 SDK가 없다 — 등록 테스트는 이걸 바꿔 배선만 검증한다.
        internal static Func<string, IAPServiceDependencies, IIAPService> ServiceFactory = CreateDefault;

        // 루트 IInstaller의 InstallBindings에서 RegisterIAPService 대신 호출한다(원스토어 빌드에서만).
        //   builder.RegisterOneStoreIAPService(licenseKey);
        //
        // licenseKey는 원스토어 개발자센터 > 앱 > 인앱 > 라이선스 관리의 공개 키다. SDK가 구매 서명 검증에 쓴다.
        // 상품은 IAP Catalog에서 읽고 스토어 ID 키는 "OneStore"다(비면 공용 ID).
        // 지급 처리기·검증기·소유 저장소는 RegisterIAPService와 같은 방식으로 선택 등록한다.
        public static ContainerBuilder RegisterOneStoreIAPService(this ContainerBuilder builder, string licenseKey,
                                                                  IAPServiceOptions options = null)
        {
            if (string.IsNullOrEmpty(licenseKey))
            {
                Debug.LogError("[OneStoreIAPService] 라이선스 키가 비어 있다. 구매 서명을 검증할 수 없어 결제가 실패한다.");
            }

            options ??= new IAPServiceOptions();

            builder.RegisterMainThreadDispatcher();

            return builder.RegisterFactory<IIAPService>(container =>
            {
                var fulfillment = container.HasBinding<IIAPFulfillment>()
                    ? container.Resolve<IIAPFulfillment>()
                    : new AutoConfirmFulfillment();

                // 구글 Tangle 검증기(CrossPlatformReceiptValidator)는 원스토어 영수증을 검증할 수 없다.
                var validator = container.HasBinding<IReceiptValidator>()
                    ? container.Resolve<IReceiptValidator>()
                    : new OneStoreReceiptValidator();

                var asyncValidator = container.HasBinding<IAsyncReceiptValidator>()
                    ? container.Resolve<IAsyncReceiptValidator>()
                    : null;

                var entitlements = container.HasBinding<IEntitlementStorage>()
                    ? container.Resolve<IEntitlementStorage>()
                    : new PlayerPrefsEntitlementStorage();

                return ServiceFactory(licenseKey, new IAPServiceDependencies
                {
                    Fulfillment = fulfillment,
                    Validator = validator,
                    AsyncValidator = asyncValidator,
                    Entitlements = entitlements,
                    Options = options,
                    Dispatcher = container.Resolve<IMainThreadDispatcher>,
                });
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        private static IIAPService CreateDefault(string licenseKey, IAPServiceDependencies dependencies) =>
            new OneStoreIAPService(CreateClient(licenseKey, dependencies.Dispatcher),
                                   UnityIAPCatalog.Resolve(dependencies.Options, OneStoreIAPService.StoreName),
                                   dependencies);

        private static IOneStoreClient CreateClient(string licenseKey, Func<IMainThreadDispatcher> dispatcher)
        {
#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android) return new OneStorePurchaseClient(licenseKey, dispatcher);
#endif
            return new UnsupportedOneStoreClient();
        }
    }
}
