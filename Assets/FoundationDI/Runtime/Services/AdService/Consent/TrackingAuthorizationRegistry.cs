using System;

namespace DarkNaku.FoundationDI
{
    // FoundationDI.iOSSupport가 자신을 등록하는 진입점. AdProviderRegistry와 같은 이유로
    // 존재한다 — 코어는 옵셔널 어셈블리를 참조할 수 없으므로(순환 참조) 반대로 이쪽이
    // 레지스트리를 들고 옵셔널 어셈블리가 [RuntimeInitializeOnLoadMethod]에서 밀어 넣는다.
    //
    // provider 레지스트리와 달리 키가 없다. ATT는 OS 권한이라 구현이 하나뿐이고, 어느
    // 광고 네트워크를 쓰든 같은 것을 묻기 때문이다.
    public static class TrackingAuthorizationRegistry
    {
        private static Func<IAdDispatcher, ITrackingAuthorization> _creator;

        // 두 번 등록하면 조용히 교체한다. 도메인 리로드와 에디터의 반복적인
        // [RuntimeInitializeOnLoadMethod] 실행이 이 경로를 실제로 여러 번 태운다.
        public static void Register(Func<IAdDispatcher, ITrackingAuthorization> creator)
        {
            _creator = creator;
        }

        // 등록된 것이 없으면 UnsupportedTrackingAuthorization으로 떨어진다. 호출자가
        // null을 신경 쓸 일이 없도록 절대 null을 돌려주지 않는다.
        public static ITrackingAuthorization Create(IAdDispatcher dispatcher)
        {
            var created = _creator?.Invoke(dispatcher);

            return created ?? new UnsupportedTrackingAuthorization();
        }

        // 테스트 전용. 정적 상태를 다음 테스트로 새어나가지 않게 TearDown에서 호출한다.
        internal static void Reset()
        {
            _creator = null;
        }
    }
}
