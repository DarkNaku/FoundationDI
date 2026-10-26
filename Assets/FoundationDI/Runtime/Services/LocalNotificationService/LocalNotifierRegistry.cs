using System;

namespace DarkNaku.FoundationDI
{
    // FoundationDI.MobileNotifications가 자신을 등록하는 진입점. 코어는 옵셔널 어셈블리를 참조할 수
    // 없으므로(순환 참조) TrackingAuthorizationRegistry와 같은 방식으로 반대쪽이 밀어 넣는다.
    public static class LocalNotifierRegistry
    {
        private static Func<LocalNotificationOptions, ILocalNotifier> _creator;

        // 두 번 등록하면 조용히 교체한다(도메인 리로드·반복되는 RuntimeInitializeOnLoadMethod).
        public static void Register(Func<LocalNotificationOptions, ILocalNotifier> creator)
        {
            _creator = creator;
        }

        // 등록된 것이 없거나 null을 돌려주면 UnsupportedLocalNotifier로 떨어진다. 절대 null이 아니다.
        public static ILocalNotifier Create(LocalNotificationOptions options)
        {
            return _creator?.Invoke(options) ?? new UnsupportedLocalNotifier();
        }

        // 테스트 전용. 정적 상태를 다음 테스트로 새어나가지 않게 한다.
        internal static void Reset()
        {
            _creator = null;
        }
    }
}
