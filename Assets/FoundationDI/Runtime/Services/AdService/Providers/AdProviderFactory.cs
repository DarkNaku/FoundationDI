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

        // SDK가 설치되고 스크립팅 심볼이 정의됐는지. 3사 어댑터를 추가할 때 여기에 심볼
        // 분기를 추가한다. Build 쪽은 손댈 필요 없다 — 옵셔널 어셈블리가
        // AdProviderRegistry.Register로 자신을 등록하면 Build가 그걸 조회한다.
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

            return Build(effective, dummyOptions);
        }

        // effective(이미 결정된 provider)를 실제 인스턴스로 소비하는 부분만 분리했다.
        // internal인 이유: Resolve가 어떤 심볼도 없이는 Dummy 외의 값을 절대 돌려주지 않아
        // Create를 통해서는 default 분기에 도달할 방법이 없다 — 이 프로젝트에는 아직
        // FOUNDATIONDI_* 심볼이 하나도 정의돼 있지 않다. 그래서 이 분기만 별도로 잘라내
        // 테스트 어셈블리(InternalsVisibleTo)가 이미 "사용 가능하다고 판단된" 상태를
        // 직접 넣어 default 분기를 검증할 수 있게 한다.
        internal IAdProvider Build(AdProviderType effective, DummyAdOptions dummyOptions)
        {
            // Dummy는 항상 내장 구현이다 — 레지스트리를 보지 않는다. 그래야 누가 실수로(또는
            // 악의적으로) Dummy에 creator를 등록해도 이 경로가 오버라이드되지 않는다.
            if (effective == AdProviderType.Dummy)
            {
                return new DummyAdProvider(_dispatcher, dummyOptions);
            }

            // 3사 어댑터(FoundationDI.AppLovin 등)는 FoundationDI를 참조하는 별도
            // 옵셔널 어셈블리라 여기서 직접 new할 수 없다(참조 방향이 반대라 순환 참조가
            // 된다). 대신 그 어셈블리가 AdProviderRegistry.Register로 스스로를 등록해
            // 두었기를 기대하고 여기서 조회만 한다.
            if (AdProviderRegistry.TryResolve(effective, out var creator))
            {
                return creator(new AdProviderCreationContext(_dispatcher));
            }

            // IsAvailable이 참(SDK 심볼 있음)이라 Resolve가 요청 그대로를 돌려줬는데
            // 레지스트리에 creator가 없는 경우. 옵셔널 어셈블리가 아예 없거나, 등록을
            // 빠뜨렸거나, IL2CPP 빌드에서 통째로 스트리핑됐다는 뜻이다. 조용히 Dummy로
            // 대체하면 이 상태를 아무도 알아채지 못한다 — 반드시 에러로 남긴다.
            // 문구는 세 서비스가 ProviderDiagnostics로 공유한다(같은 사고, 같은 꼴).
            Debug.LogError(ProviderDiagnostics.MissingCreator(
                "AdService", effective.ToString(), "Dummy provider로 대체한다."));
            return new DummyAdProvider(_dispatcher, dummyOptions);
        }
    }
}
