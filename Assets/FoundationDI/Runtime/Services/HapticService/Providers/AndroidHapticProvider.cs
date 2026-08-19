#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>Android Vibrator/VibrationEffect 기반 provider.
    /// 진폭 제어(hasAmplitudeControl)를 런타임 확인해 지원 시 커브/웨이브폼, 아니면 프리셋 폴백.</summary>
    public class AndroidHapticProvider : IHapticProvider
    {
        private readonly AndroidJavaObject _vibrator;
        private readonly int _api;
        private readonly bool _hasVibrator;
        private readonly bool _hasAmplitude;

        public AndroidHapticProvider()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                using var ver = new AndroidJavaClass("android.os.Build$VERSION");
                _api = ver.GetStatic<int>("SDK_INT");

                _hasVibrator = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
                _hasAmplitude = _api >= 26 && _vibrator != null && _vibrator.Call<bool>("hasAmplitudeControl");
            }
            catch (System.Exception e) { Debug.LogError(e); }
        }

        public void Impact(HapticImpact style)
        {
            switch (style)
            {
                case HapticImpact.Light: OneShot(40); break;
                case HapticImpact.Soft: OneShot(45); break;
                case HapticImpact.Heavy: OneShot(90); break;
                case HapticImpact.Rigid: OneShot(75); break;
                default: OneShot(60); break; // Medium
            }
        }

        public void Notification(HapticNotification type)
        {
            switch (type)
            {
                case HapticNotification.Success: Waveform(new long[] { 0, 50 }, null); break;
                case HapticNotification.Warning: Waveform(new long[] { 0, 50, 90, 50 }, null); break;
                default: Waveform(new long[] { 0, 70, 90, 70, 90, 70 }, null); break; // Error
            }
        }

        public void Selection() => OneShot(30);

        public void Prewarm() { /* Android는 워밍 불필요 */ }

        public void Stop()
        {
            if (_vibrator == null) return;
            try { _vibrator.Call("cancel"); } catch { }
        }

        public async Awaitable PlayAsync(HapticCurve curve, CancellationToken ct)
        {
            var k = Sanitize(curve.Android);
            if (k.DelayMs > 0L) await Awaitable.WaitForSecondsAsync(k.DelayMs / 1000f, ct);

            if (!_hasVibrator) return;
            if (!_hasAmplitude) { Impact(k.Fallback); return; }

            BuildCurveWaveform(k, out long[] timings, out int[] amplitudes);
            Waveform(timings, amplitudes);
            await Awaitable.WaitForSecondsAsync(k.DurationMs / 1000f, ct);
        }

        public async Awaitable PlayAsync(HapticPattern pattern, CancellationToken ct)
        {
            var seq = pattern.Android;
            if (seq == null || seq.Length == 0 || !_hasVibrator) return;

            long[] timings = new long[seq.Length * 2];
            int[] amplitudes = new int[seq.Length * 2];
            long total = 0L;
            for (int i = 0; i < seq.Length; i++)
            {
                long delay = System.Math.Max(0L, seq[i].DelayMs);
                long pulse = System.Math.Max(1L, seq[i].PulseMs);
                timings[i * 2] = delay; amplitudes[i * 2] = 0;
                timings[i * 2 + 1] = pulse; amplitudes[i * 2 + 1] = Mathf.Clamp(seq[i].Amplitude, 0, 255);
                total += delay + pulse;
            }

            Waveform(timings, _hasAmplitude ? amplitudes : null);
            await Awaitable.WaitForSecondsAsync(total / 1000f, ct);
        }

        private void OneShot(long ms)
        {
            if (!_hasVibrator) return;
            try
            {
                if (_api >= 26)
                {
                    using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    int amp = _hasAmplitude ? effectClass.GetStatic<int>("DEFAULT_AMPLITUDE") : -1;
                    using var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amp);
                    _vibrator.Call("vibrate", effect);
                }
                else { _vibrator.Call("vibrate", ms); }
            }
            catch (System.Exception e) { Debug.LogError(e); }
        }

        private void Waveform(long[] timings, int[] amplitudes)
        {
            if (!_hasVibrator || timings == null || timings.Length == 0) return;
            try
            {
                if (_api >= 26)
                {
                    using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    using AndroidJavaObject effect = (amplitudes != null && amplitudes.Length == timings.Length)
                        ? effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1)
                        : effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, -1);
                    _vibrator.Call("vibrate", effect);
                }
                else { _vibrator.Call("vibrate", timings, -1); }
            }
            catch (System.Exception e) { Debug.LogError(e); }
        }

        private static AndroidHapticCurve Sanitize(AndroidHapticCurve c)
        {
            c.DurationMs = c.DurationMs <= 0L ? 160L : System.Math.Max(10L, c.DurationMs);
            c.MaxAmplitude = Mathf.Clamp(c.MaxAmplitude <= 0 ? 255 : c.MaxAmplitude, 1, 255);
            c.Samples = Mathf.Clamp(c.Samples <= 0 ? 16 : c.Samples, 2, 64);
            c.DelayMs = System.Math.Max(0L, c.DelayMs);
            if (c.Intensity == null || c.Intensity.length == 0)
            {
                c.Intensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }

            return c;
        }

        private static void BuildCurveWaveform(AndroidHapticCurve c, out long[] timings, out int[] amplitudes)
        {
            int durMs = (int)System.Math.Max(10L, c.DurationMs);
            int count = Mathf.Clamp(c.Samples, 2, Mathf.Min(64, durMs));
            timings = new long[count];
            amplitudes = new int[count];
            long remaining = durMs;
            for (int i = 0; i < count; i++)
            {
                long seg = System.Math.Max(1L, remaining / (count - i));
                remaining -= seg;
                float n = (i + 0.5f) / count;
                float intensity = Mathf.Clamp01(c.Intensity.Evaluate(n));
                timings[i] = seg;
                amplitudes[i] = Mathf.Clamp(Mathf.RoundToInt(intensity * c.MaxAmplitude), 0, 255);
            }
        }
    }
}
#endif
