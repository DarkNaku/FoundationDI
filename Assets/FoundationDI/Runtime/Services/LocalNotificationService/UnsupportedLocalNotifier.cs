using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 에디터·미지원 플랫폼·모바일 알림 패키지가 없을 때. 권한은 있다고 보고 예약은 하지 않는다 —
    // 에디터에서 일정 계산까지는 돌아가 서비스가 예약 개수를 로그로 남긴다.
    public sealed class UnsupportedLocalNotifier : ILocalNotifier
    {
        public bool HasPermission => true;

        public Awaitable<bool> RequestPermissionAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public void CancelAll()
        {
        }

        public void Schedule(LocalNotification notification)
        {
        }
    }
}
