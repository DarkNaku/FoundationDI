using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class AdProviderFactory : IAdProviderFactory
    {
        private readonly IAdDispatcher _dispatcher;

        public AdProviderFactory(IAdDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        // SDK가 설치되고 스크립팅 심볼이 정의됐는지. 3사 어댑터를 추가할 때
        // 여기와 Create만 손대면 된다.
        public static bool IsAvailable(AdProviderType type)
        {
            switch (type)
            {
                case AdProviderType.Dummy:
                    return true;
                case AdProviderType.AdMob:
#if FOUNDATIONDI_ADMOB
                    return true;
#else
                    return false;
#endif
                case AdProviderType.LevelPlay:
#if FOUNDATIONDI_LEVELPLAY
                    return true;
#else
                    return false;
#endif
                case AdProviderType.AppLovin:
#if FOUNDATIONDI_APPLOVIN
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        // "무엇을 쓸지"만 결정하는 순수 함수. 인스턴스를 만들지 않으므로 테스트가 쉽다.
        public static AdProviderType Resolve(AdProviderType requested, bool forceDummy, out string warning)
        {
            warning = null;

            // 강제 더미는 의도된 설정이다. 경고하면 매 실행마다 소음이 된다.
            if (forceDummy) return AdProviderType.Dummy;

            if (IsAvailable(requested)) return requested;

            warning = $"[AdService] {requested} provider를 요청했지만 SDK 또는 스크립팅 심볼이 없다. " +
                      $"Dummy provider로 대체한다. (필요한 심볼: FOUNDATIONDI_{requested.ToString().ToUpperInvariant()})";
            return AdProviderType.Dummy;
        }

        public IAdProvider Create(AdProviderType type, DummyAdOptions dummyOptions, bool forceDummy)
        {
            var effective = Resolve(type, forceDummy, out var warning);

            if (warning != null) Debug.LogWarning(warning);

            // Resolve가 고른 effective를 실제로 소비한다. 3사 어댑터가 추가되면 여기에 case가 늘어난다.
            switch (effective)
            {
                case AdProviderType.Dummy:
                    return new DummyAdProvider(_dispatcher, dummyOptions);
                default:
                    // IsAvailable이 참(SDK 심볼 있음)이라 Resolve가 요청 그대로를 돌려줬는데
                    // 여기에 아직 분기가 없는 경우. 조용히 Dummy로 대체하면 이 상태를 아무도
                    // 알아채지 못한다 — 반드시 에러로 남긴다.
                    Debug.LogError($"[AdService] {effective} provider는 사용 가능하다고 판단됐지만 " +
                                  "AdProviderFactory.Create에 아직 구현되지 않았다. Dummy provider로 대체한다.");
                    return new DummyAdProvider(_dispatcher, dummyOptions);
            }
        }
    }
}
