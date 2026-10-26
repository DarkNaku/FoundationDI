using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class PlayerPrefsLocalNotificationStorage : ILocalNotificationStorage
    {
        private const string ENABLED_KEY = "FoundationDI.LocalNotification.Enabled";
        private const string ASKED_KEY = "FoundationDI.LocalNotification.AskedPermission";

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
    }
}
