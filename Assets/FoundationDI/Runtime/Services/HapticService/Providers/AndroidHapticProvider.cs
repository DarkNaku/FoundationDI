#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Android Vibrator/VibrationEffect 기반 provider.
    /// VIBRATE 권한은 패키지의 FoundationDIHaptic.androidlib 가 병합 제공한다.
    ///
    /// 진동 방식 주의: VibrationEffect.createPredefined(EFFECT_*) 는 벤더가 해당 프리셋을
    /// 구현한 기기에서만 동작하고 그 외에는 조용히 무시된다. 따라서 실제 진동이 보장되는
    /// createOneShot(지속시간+DEFAULT_AMPLITUDE) / createWaveform 를 사용한다.
    /// (API 26 미만은 legacy Vibrator.vibrate 로 폴백.)
    /// </summary>
    public class AndroidHapticProvider : IHapticProvider
    {
        private readonly AndroidJavaObject _vibrator;
        private readonly bool _supportsEffect; // VibrationEffect(createOneShot/Waveform) = API 26+
        private readonly int _defaultAmplitude;

        public AndroidHapticProvider()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            _supportsEffect = version.GetStatic<int>("SDK_INT") >= 26;

            if (_supportsEffect)
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                _defaultAmplitude = effectClass.GetStatic<int>("DEFAULT_AMPLITUDE");
            }
        }

        public void Impact(HapticImpact style)
        {
            switch (style)
            {
                // ERM 모터는 진폭 제어가 없어 지속시간만이 세기 차별 요소다.
                // 또한 ~30ms 미만은 모터 관성 탓에 지각되지 않으므로 하한을 40ms로 둔다.
                case HapticImpact.Light:
                    Vibrate(40);
                    break;
                case HapticImpact.Soft:
                    Vibrate(45);
                    break;
                case HapticImpact.Heavy:
                    Vibrate(90);
                    break;
                case HapticImpact.Rigid:
                    Vibrate(75);
                    break;
                default: // Medium
                    Vibrate(60);
                    break;
            }
        }

        public void Notification(HapticNotification type)
        {
            switch (type)
            {
                case HapticNotification.Success:
                    VibratePattern(new long[] { 0, 50 });
                    break;
                case HapticNotification.Warning:
                    VibratePattern(new long[] { 0, 50, 90, 50 });
                    break;
                default: // Error
                    VibratePattern(new long[] { 0, 70, 90, 70, 90, 70 });
                    break;
            }
        }

        public void Selection()
        {
            // 가장 가벼운 틱. 단, ERM 지각 하한(~30ms) 위로 둔다.
            Vibrate(30);
        }

        // 단발 진동. createOneShot 은 지속시간+진폭으로 실제 진동을 보장한다.
        private void Vibrate(long milliseconds)
        {
            if (_vibrator == null) return;

            if (_supportsEffect)
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot", milliseconds, _defaultAmplitude);
                _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", milliseconds);
            }
        }

        // 패턴 진동(off, on, off, on ...). repeat = -1 은 1회 재생.
        private void VibratePattern(long[] pattern)
        {
            if (_vibrator == null) return;

            if (_supportsEffect)
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effectClass.CallStatic<AndroidJavaObject>(
                    "createWaveform", pattern, -1);
                _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", pattern, -1);
            }
        }
    }
}
#endif
