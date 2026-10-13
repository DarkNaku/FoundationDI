using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal static class UmpConsentFactory
    {
        // 실기가 아니거나 네이티브 SDK가 빠졌으면 UnsupportedUmpConsent로 떨어진다 — 에디터에서도
        // 광고·분석 흐름이 막히지 않게 한다.
        public static IUmpConsent Create(IMainThreadDispatcher dispatcher)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return (IUmpConsent)AndroidUmpConsent.TryCreate(dispatcher) ?? new UnsupportedUmpConsent();
#elif UNITY_IOS && !UNITY_EDITOR
            if (IosUmpConsent.IsAvailable) return new IosUmpConsent(dispatcher);

            Debug.LogWarning("[ConsentService] GoogleUserMessagingPlatform 포드가 링크되지 않았다. UMP 없이 진행한다. " +
                             "Use UMP를 켰다면 pod install이 됐는지 확인한다.");
            return new UnsupportedUmpConsent();
#else
            return new UnsupportedUmpConsent();
#endif
        }
    }
}
