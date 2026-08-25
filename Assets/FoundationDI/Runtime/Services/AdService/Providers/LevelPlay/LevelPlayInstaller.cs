using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // FoundationDI(코어 어셈블리)는 이 옵셔널 어셈블리를 참조할 수 없으므로(순환 참조),
    // 반대로 이쪽이 도메인 로드 시점에 스스로를 AdProviderRegistry에 등록한다.
    // AdProviderFactory.Build가 조회한다(AppLovinInstaller와 같은 구조).
    internal static class LevelPlayInstaller
    {
        // 도메인 리로드를 끈 프로젝트(Enter Play Mode Options)에서는 정적 필드가 플레이 세션
        // 사이에 살아남는다. 초기화 래치가 이전 세션의 "이미 초기화됨"을 들고 있으면 새 세션의
        // InitializeAsync가 실제로는 초기화되지 않은 SDK를 초기화됐다고 답한다.
        // SubsystemRegistration은 BeforeSceneLoad보다 먼저 도는 유일한 단계다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LevelPlayAdProvider.ResetInitLatch();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AdProviderRegistry.Register(AdProviderType.LevelPlay,
                ctx => new LevelPlayAdProvider(ctx.Dispatcher));

            // **반드시 BeforeSceneLoad에서 건다.**
            //
            // LevelPlay는 LevelPlayMediationSettings의 EnableIronsourceSDKInitAPI가 켜져 있으면
            // 앱이 직접 부르지 않아도 스스로 LevelPlay.Init을 호출한다. 그 호출은
            // LevelPlayAutoInitializer의 [RuntimeInitializeOnLoadMethod]에서 일어나는데, 인자
            // 없는 이 속성의 기본값은 AfterSceneLoad다(Runtime/Utilities/LevelPlayAutoInitializer.cs).
            // 즉 초기화 성공 콜백이 provider 인스턴스가 만들어지기 한참 전에 이미 지나갈 수 있다.
            //
            // LevelPlay에는 MaxSdk.IsInitialized()에 해당하는 "이미 초기화됐는지" 조회 API가
            // 없으므로, 지나간 콜백을 나중에 확인할 방법은 미리 구독해 기록해 두는 것뿐이다.
            // 여기서 걸어야 자동 초기화보다 먼저 자리를 잡는다.
            LevelPlayAdProvider.InstallInitLatch();
        }
    }
}
