using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // FoundationDI(코어 어셈블리)는 이 옵셔널 어셈블리를 참조할 수 없으므로(순환 참조),
    // 반대로 이쪽이 도메인 로드 시점에 스스로를 AnalyticsProviderRegistry에 등록한다.
    internal static class FirebaseInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AnalyticsProviderRegistry.Register(AnalyticsProviderType.Firebase,
                                               _ => new FirebaseAnalyticsProvider());
        }
    }
}
