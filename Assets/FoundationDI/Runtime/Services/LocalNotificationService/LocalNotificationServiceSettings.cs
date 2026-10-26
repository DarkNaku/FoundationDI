using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "LocalNotificationServiceSettings",
                     menuName = "FoundationDI/Local Notification Service Settings")]
    public sealed class LocalNotificationServiceSettings : ScriptableObject
    {
        // 모바일 알림 설정에 등록하는 아이콘 리소스 이름. 런타임과 에디터가 함께 쓴다.
        public const string ANDROID_SMALL_ICON_ID = "fdi_notification_small";

        [Header("Android")]
        [Tooltip("알림 채널 ID. 한 번 출시하면 바꾸지 않는다 — 바꾸면 유저가 정한 채널 설정이 새 채널로 이어지지 않는다.")]
        [SerializeField] private string _androidChannelId = "default";

        [Tooltip("시스템 설정 앱에 보이는 채널 이름.")]
        [SerializeField] private string _androidChannelName = "Notifications";

        [SerializeField] private string _androidChannelDescription = "Notifications";

        [Tooltip("상태 표시줄 작은 아이콘(흰색 실루엣 + 투명 배경). 비우면 앱 아이콘이 흰 사각형으로 보인다. " +
                 "빌드 때 FoundationDI.MobileNotifications.Editor가 모바일 알림 설정에 넣는다.")]
        [SerializeField] private Texture2D _androidSmallIcon;

        public string AndroidChannelId => _androidChannelId;
        public string AndroidChannelName => _androidChannelName;
        public string AndroidChannelDescription => _androidChannelDescription;
        public Texture2D AndroidSmallIcon => _androidSmallIcon;

        public LocalNotificationOptions ToOptions()
        {
            return new LocalNotificationOptions(_androidChannelId, _androidChannelName, _androidChannelDescription,
                                                _androidSmallIcon != null ? ANDROID_SMALL_ICON_ID : null);
        }
    }
}
