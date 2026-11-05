using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class PlayerPrefsLocalNotificationStorage : ILocalNotificationStorage
    {
        private const string ENABLED_KEY = "FoundationDI.LocalNotification.Enabled";
        private const string ASKED_KEY = "FoundationDI.LocalNotification.AskedPermission";
        private const string OPENED_KEY = "FoundationDI.LocalNotification.LastOpened";

        // 기본은 켜짐이다. 권한은 OS가 따로 묻는다.
        public bool IsEnabled
        {
            get => PlayerPrefs.GetInt(ENABLED_KEY, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(ENABLED_KEY, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public bool HasAskedPermission
        {
            get => PlayerPrefs.GetInt(ASKED_KEY, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(ASKED_KEY, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // 마지막으로 알린 데이터 하나만 기억한다. 같은 알림이 두 번 오지 않게 하는 것이 목적이므로 충분하다 —
        // 서로 다른 알림이 같은 데이터를 갖지 않게 하는 것은 게임 몫이다(울릴 시각을 넣는 등).
        public bool TryMarkOpened(string data)
        {
            if (string.IsNullOrEmpty(data)) return false;
            if (PlayerPrefs.GetString(OPENED_KEY, null) == data) return false;

            PlayerPrefs.SetString(OPENED_KEY, data);
            PlayerPrefs.Save();
            return true;
        }
    }
}
