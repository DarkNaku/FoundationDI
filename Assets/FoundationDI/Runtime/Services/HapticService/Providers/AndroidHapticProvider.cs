#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Android Vibrator/VibrationEffect 기반 provider.
    /// AndroidManifest.xml에 android.permission.VIBRATE 권한이 필요하다.
    /// </summary>
    public class AndroidHapticProvider : IHapticProvider
    {
        // VibrationEffect predefined effect id (API 29+)
        private const int EFFECT_TICK = 2;
        private const int EFFECT_CLICK = 0;
        private const int EFFECT_HEAVY_CLICK = 5;

        private readonly AndroidJavaObject _vibrator;
        private readonly bool _supportsEffect;

        public AndroidHapticProvider()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            _supportsEffect = version.GetStatic<int>("SDK_INT") >= 29;
        }

        public void Impact(HapticImpact style)
        {
            switch (style)
            {
                case HapticImpact.Light:
                case HapticImpact.Soft:
                    Play(EFFECT_TICK, 20);
                    break;
                case HapticImpact.Heavy:
                case HapticImpact.Rigid:
                    Play(EFFECT_HEAVY_CLICK, 60);
                    break;
                default: // Medium
                    Play(EFFECT_CLICK, 40);
                    break;
            }
        }

        public void Notification(HapticNotification type)
        {
            switch (type)
            {
                case HapticNotification.Success:
                    Play(EFFECT_CLICK, 40);
                    break;
                default: // Warning / Error
                    Play(EFFECT_HEAVY_CLICK, 60);
                    break;
            }
        }

        public void Selection()
        {
            Play(EFFECT_TICK, 15);
        }

        private void Play(int effectId, long fallbackMs)
        {
            if (_vibrator == null) return;

            if (_supportsEffect)
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effectClass.CallStatic<AndroidJavaObject>("createPredefined", effectId);
                _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", fallbackMs);
            }
        }
    }
}
#endif
