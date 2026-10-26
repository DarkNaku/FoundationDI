using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 코어는 이 어셈블리를 참조할 수 없으므로 이쪽이 먼저 자신을 알린다.
    // BeforeSceneLoad여야 첫 씬의 IInstaller보다 앞선다.
    public static class MobileNotificationsInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // 에디터의 알림 센터는 실제로 아무것도 울리지 않는다. 등록하지 않으면 UnsupportedLocalNotifier로
            // 떨어져 예약 개수 로그만 남는다.
            if (Application.isEditor) return;

            LocalNotifierRegistry.Register(options => new MobileLocalNotifier(options));
        }
    }
}
