using System.Collections.Generic;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class ConsentServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다. AdService·AnalyticsService보다 앞이든 뒤든 상관없다.
        //   builder.RegisterConsentService(_consentSettings);                 // 기본 약관 창
        //   builder.RegisterConsentService(_consentSettings, prompt: myPrompt); // 커스텀 약관 창
        public static ContainerBuilder RegisterConsentService(this ContainerBuilder builder,
                                                              ConsentServiceSettings settings,
                                                              IPolicyAgreementPrompt prompt = null)
        {
            if (settings == null)
            {
                Debug.LogError("[ConsentService] ConsentServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterMainThreadDispatcher();
            builder.RegisterType(typeof(PlayerPrefsPolicyAgreementStorage), new[] { typeof(IPolicyAgreementStorage) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IConsentService>(container =>
            {
                var dispatcher = container.Resolve<IMainThreadDispatcher>();
                var storage = container.Resolve<IPolicyAgreementStorage>();

                // 선행 작업(분석 SDK 초기화 등)은 해석 시점에 모은다. 등록 순서와 무관하게 잡힌다.
                var prerequisites = new List<IConsentPrerequisite>(container.All<IConsentPrerequisite>());

                var ump = settings.UseUmp ? UmpConsentFactory.Create(dispatcher) : new UnsupportedUmpConsent();
                var tracking = TrackingAuthorizationRegistry.Create(dispatcher);
                var resolvedPrompt = prompt ?? new DefaultPolicyAgreementPrompt(settings.PromptPrefab, new UnityUrlOpener());

                return new ConsentService(settings.ToOptions(Debug.isDebugBuild), resolvedPrompt, storage, ump,
                                          tracking, dispatcher, prerequisites);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
