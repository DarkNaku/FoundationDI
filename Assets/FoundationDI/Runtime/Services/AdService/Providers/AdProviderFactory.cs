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
        // 여기와 CreateReal만 손대면 된다.
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

            // 3사 어댑터가 추가되면 여기에 분기가 생긴다. 지금은 Dummy만 존재한다.
            return new DummyAdProvider(_dispatcher, dummyOptions);
        }
    }
}
