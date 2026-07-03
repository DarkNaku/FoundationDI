using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IHapticService : IDisposable
    {
        bool Enabled { get; set; }
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();
    }

    public class HapticService : IHapticService
    {
        private const string HAPTIC_ENABLED = "HAPTIC_ENABLED";

        private readonly IHapticProvider _provider;

        public HapticService() : this(CreatePlatformProvider())
        {
        }

        public HapticService(IHapticProvider provider)
        {
            _provider = provider;
        }

        private static IHapticProvider CreatePlatformProvider()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new iOSHapticProvider();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidHapticProvider();
#else
            return new NoopHapticProvider();
#endif
        }

        public bool Enabled
        {
            get => PlayerPrefs.GetInt(HAPTIC_ENABLED, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(HAPTIC_ENABLED, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public void Impact(HapticImpact style)
        {
            if (Enabled) _provider.Impact(style);
        }

        public void Notification(HapticNotification type)
        {
            if (Enabled) _provider.Notification(type);
        }

        public void Selection()
        {
            if (Enabled) _provider.Selection();
        }

        public void Dispose()
        {
        }
    }
}
