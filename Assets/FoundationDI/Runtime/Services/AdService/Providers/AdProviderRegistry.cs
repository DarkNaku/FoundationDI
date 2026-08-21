using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // provider 생성에 필요한 것들을 담는다. 오늘은 IAdDispatcher 하나뿐이지만, 나중에 두
    // 번째 의존성(예: 로깅 seam)이 생겨도 이 struct에 프로퍼티만 추가하면 되고 이미 등록된
    // 모든 creator 델리게이트의 시그니처는 그대로 유지된다 — 파라미터 목록이었다면 이 변경이
    // 모든 호출부를 깨뜨렸을 것이다.
    public readonly struct AdProviderCreationContext
    {
        public IAdDispatcher Dispatcher { get; }

        public AdProviderCreationContext(IAdDispatcher dispatcher)
        {
            Dispatcher = dispatcher;
        }
    }

    // FoundationDI.AppLovin 같은 SDK별 옵셔널 어셈블리가 자신을 등록하는 진입점이다.
    // FoundationDI는 그 어셈블리를 참조할 수 없다(순환 참조) — 그래서 반대로 이쪽이
    // "누가 이 타입을 만들 줄 아는가"를 물어보는 레지스트리를 들고, 옵셔널 어셈블리가
    // [RuntimeInitializeOnLoadMethod]에서 자신을 밀어 넣는다. AdProviderFactory.Build가
    // 소비한다.
    public static class AdProviderRegistry
    {
        private static readonly Dictionary<AdProviderType, Func<AdProviderCreationContext, IAdProvider>> _creators = new();

        // 같은 타입을 두 번 등록하면 예외 없이 조용히 교체한다. 도메인 리로드나 에디터의
        // 반복적인 [RuntimeInitializeOnLoadMethod] 실행 둘 다 이 경로를 실제로 두 번 이상
        // 태운다 — 예외를 던지면 그 자체가 크래시가 되고, 중복 등록을 허용하면 어느 쪽이
        // 이겼는지 알 수 없는 상태가 된다.
        public static void Register(AdProviderType type, Func<AdProviderCreationContext, IAdProvider> creator)
        {
            _creators[type] = creator;
        }

        internal static bool TryResolve(AdProviderType type, out Func<AdProviderCreationContext, IAdProvider> creator)
        {
            return _creators.TryGetValue(type, out creator);
        }

        // 테스트 전용. 정적 상태를 다음 테스트로 새어나가지 않게 TearDown에서 호출한다.
        internal static void Reset()
        {
            _creators.Clear();
        }
    }
}
