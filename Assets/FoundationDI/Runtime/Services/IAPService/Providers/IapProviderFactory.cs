using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class IapProviderFactory : IIapProviderFactory
    {
        // SDK가 설치되고 스크립팅 심볼이 정의됐는지. provider를 추가할 때 여기에 심볼 분기를 추가한다.
        public static bool IsAvailable(IapProviderType type)
        {
            switch (type)
            {
                case IapProviderType.Dummy:
                    return true;
                case IapProviderType.UnityIAP:
#if FOUNDATIONDI_UNITYIAP
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        // "무엇을 쓸지"만 결정하는 순수 함수. 인스턴스를 만들지 않으므로 테스트가 쉽다.
        public static IapProviderType Resolve(IapProviderType requested, bool forceDummy, out string warning)
        {
            warning = null;

            // 강제 더미는 의도된 설정이다. 경고하면 매 실행마다 소음이 된다.
            if (forceDummy) return IapProviderType.Dummy;

            if (IsAvailable(requested)) return requested;

            warning = $"[IAPService] {requested} provider를 요청했지만 SDK 또는 스크립팅 심볼이 없다. " +
                      "Dummy provider로 대체한다. (필요한 심볼: FOUNDATIONDI_UNITYIAP)";
            return IapProviderType.Dummy;
        }

        public IIapProvider Create(IapProviderType type, DummyIapOptions dummyOptions, bool forceDummy)
        {
            var effective = Resolve(type, forceDummy, out var warning);

            if (warning != null) Debug.LogWarning(warning);

            return Build(effective, dummyOptions);
        }

        // Resolve가 심볼 없이는 Dummy 외의 값을 돌려주지 않으므로 Create를 통해서는 아래
        // 폴백 경로에 도달할 방법이 없다. 그래서 이 부분만 잘라내 테스트 어셈블리가
        // "이미 사용 가능하다고 판단된" 상태를 직접 넣어 검증할 수 있게 한다.
        internal IIapProvider Build(IapProviderType effective, DummyIapOptions dummyOptions)
        {
            // Dummy는 항상 내장 구현이다 — 레지스트리를 보지 않는다. 누가 실수로 Dummy에
            // creator를 등록해도 이 경로가 오버라이드되지 않는다.
            if (effective == IapProviderType.Dummy) return new DummyIapProvider(dummyOptions);

            if (IapProviderRegistry.TryResolve(effective, out var creator))
            {
                return creator(new IapProviderCreationContext());
            }

            // 심볼은 정의됐는데 아무도 등록하지 않았다는 뜻이다 — 어댑터 어셈블리가 없거나,
            // IL2CPP 빌드에서 통째로 스트리핑됐거나. 조용히 Dummy로 대체하면 아무도 이 상태를
            // 알아채지 못한다 — 반드시 에러로 남긴다.
            Debug.LogError(ProviderDiagnostics.MissingCreator(
                "IAPService", effective.ToString(), "Dummy provider로 대체한다."));
            return new DummyIapProvider(dummyOptions);
        }
    }
}
