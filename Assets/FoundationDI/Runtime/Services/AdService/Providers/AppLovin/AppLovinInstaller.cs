using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // FoundationDI(코어 어셈블리)는 이 옵셔널 어셈블리를 참조할 수 없으므로(순환 참조),
    // 반대로 이쪽이 도메인 로드 시점에 스스로를 AdProviderRegistry에 등록한다.
    // AdProviderFactory.Build가 조회한다(README §5, AdProviderRegistry.cs 참고).
    internal static class AppLovinInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AdProviderRegistry.Register(AdProviderType.AppLovin,
                ctx => new AppLovinAdProvider(ctx.Dispatcher));
        }
    }
}
