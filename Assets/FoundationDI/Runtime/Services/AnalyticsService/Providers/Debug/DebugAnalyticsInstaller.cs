using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Debug provider는 FoundationDI 어셈블리 안에 있어서 팩토리가 직접 new 할 수도 있다.
    // 그러지 않고 다른 provider와 똑같이 레지스트리를 거치게 한 이유는, 팩토리에 "Debug만
    // 특별대우"하는 분기가 생기는 순간 그 분기가 곧 규칙의 예외가 되기 때문이다.
    // 모든 provider가 같은 경로로 들어오면 팩토리는 조회 하나만 알면 된다.
    internal static class DebugAnalyticsInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug,
                                               _ => new DebugAnalyticsProvider());
        }
    }
}
