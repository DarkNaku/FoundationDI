using UnityEngine;
using Unity.Notifications;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#elif UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace DarkNaku.FoundationDI
{
    // com.unity.mobile.notifications의 공통 API(NotificationCenter)로 예약한다.
    //
    // 권한 확인만 플랫폼 API를 쓴다 — 공통 API의 RequestPermission은 아직 묻지 않았으면 창을 띄우므로
    // "지금 권한이 있는가"를 확인하는 데 쓸 수 없다.
    public sealed class MobileLocalNotifier : ILocalNotifier
    {
        // 생성자에서 알림 센터를 초기화한다. 안드로이드 채널은 이때 만들어진다(Alert → 중요도 High).
        public MobileLocalNotifier(LocalNotificationOptions options)
        {
            var args = NotificationCenterArgs.Default;
            args.AndroidChannelId = options.AndroidChannelId;
            args.AndroidChannelName = options.AndroidChannelName;
            args.AndroidChannelDescription = options.AndroidChannelDescription;
            args.PresentationOptions = NotificationPresentation.Alert | NotificationPresentation.Sound;

            if (!string.IsNullOrEmpty(options.AndroidSmallIcon)) args.AndroidSmallIcon = options.AndroidSmallIcon;

            NotificationCenter.Initialize(args);
        }

#if UNITY_ANDROID
        public bool HasPermission => AndroidNotificationCenter.UserPermissionToPost == PermissionStatus.Allowed;
#elif UNITY_IOS
        public bool HasPermission =>
            iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus == AuthorizationStatus.Authorized;
#else
        public bool HasPermission => false;
#endif

        public async Awaitable<bool> RequestPermissionAsync()
        {
            var request = NotificationCenter.RequestPermission();

            // 권한 창이 떠 있는 동안 iOS는 플레이어 루프가 멈춘다. 닫히면 다시 돈다.
            while (request.Status == NotificationsPermissionStatus.RequestPending)
            {
                await Awaitable.NextFrameAsync();
            }

            return request.Status == NotificationsPermissionStatus.Granted;
        }

        public void CancelAll()
        {
            NotificationCenter.CancelAllScheduledNotifications();
        }

        public void Schedule(LocalNotification notification)
        {
            var native = new Notification { Title = notification.Title, Text = notification.Body };

            NotificationCenter.ScheduleNotification(native, new NotificationDateTimeSchedule(notification.FireTime));
        }
    }
}
