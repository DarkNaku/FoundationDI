#if UNITY_IOS && !UNITY_EDITOR
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>iOS UIFeedbackGenerator(프리셋) + CoreHaptics(커브) 기반 provider.
    /// 네이티브 브리지: Plugins/iOS/FDI_Haptic.mm</summary>
    public class iOSHapticProvider : IHapticProvider
    {
        [DllImport("__Internal")] private static extern void FDI_HapticImpact(int style);
        [DllImport("__Internal")] private static extern void FDI_HapticNotification(int type);
        [DllImport("__Internal")] private static extern void FDI_HapticSelection();
        [DllImport("__Internal")] private static extern void FDI_HapticPrewarm();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_HapticSupportsCore();
        [DllImport("__Internal")] private static extern void FDI_HapticPlayCurve(
            float durationSeconds, float sharpness, float[] times, float[] intensities, int count);
        [DllImport("__Internal")] private static extern void FDI_HapticStopCurve();

        public void Impact(HapticImpact style) { try { FDI_HapticImpact((int)style); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Notification(HapticNotification type) { try { FDI_HapticNotification((int)type); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Selection() { try { FDI_HapticSelection(); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Prewarm() { try { FDI_HapticPrewarm(); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Stop() { try { FDI_HapticStopCurve(); } catch { } }

        public async Awaitable PlayAsync(HapticCurve curve, CancellationToken ct)
        {
            var k = Sanitize(curve.IOS);
            if (k.DelayMs > 0f) await Awaitable.WaitForSecondsAsync(k.DelayMs / 1000f, ct);

            if (!FDI_HapticSupportsCore()) { Impact(k.Fallback); return; }

            Sample(k, out float[] times, out float[] intensities);
            try { FDI_HapticPlayCurve(k.DurationMs / 1000f, k.Sharpness, times, intensities, k.Samples); }
            catch (System.Exception e) { Debug.LogError(e); Impact(k.Fallback); return; }

            await Awaitable.WaitForSecondsAsync(k.DurationMs / 1000f, ct);
        }

        public async Awaitable PlayAsync(HapticPattern pattern, CancellationToken ct)
        {
            var seq = pattern.IOS;
            if (seq == null) return;
            for (int i = 0; i < seq.Length; i++)
            {
                float delay = Mathf.Max(0f, seq[i].DelayMs);
                if (delay > 0f) await Awaitable.WaitForSecondsAsync(delay / 1000f, ct);
                ct.ThrowIfCancellationRequested();
                FirePreset(seq[i].Preset);
            }
        }

        private void FirePreset(HapticPreset p)
        {
            switch (p)
            {
                case HapticPreset.Selection: Selection(); break;
                case HapticPreset.Success: Notification(HapticNotification.Success); break;
                case HapticPreset.Warning: Notification(HapticNotification.Warning); break;
                case HapticPreset.Error: Notification(HapticNotification.Error); break;
                case HapticPreset.LightImpact: Impact(HapticImpact.Light); break;
                case HapticPreset.MediumImpact: Impact(HapticImpact.Medium); break;
                case HapticPreset.HeavyImpact: Impact(HapticImpact.Heavy); break;
                case HapticPreset.SoftImpact: Impact(HapticImpact.Soft); break;
                case HapticPreset.RigidImpact: Impact(HapticImpact.Rigid); break;
            }
        }

        private static iOSHapticCurve Sanitize(iOSHapticCurve c)
        {
            c.DurationMs = c.DurationMs <= 0f ? 160f : Mathf.Max(10f, c.DurationMs);
            c.Sharpness = Mathf.Clamp01(c.Sharpness);
            c.Samples = Mathf.Clamp(c.Samples <= 0 ? 16 : c.Samples, 2, 64);
            c.DelayMs = Mathf.Max(0f, c.DelayMs);
            if (c.Intensity == null || c.Intensity.length == 0)
                c.Intensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            return c;
        }

        private static void Sample(iOSHapticCurve c, out float[] times, out float[] intensities)
        {
            float durSec = c.DurationMs / 1000f;
            times = new float[c.Samples];
            intensities = new float[c.Samples];
            for (int i = 0; i < c.Samples; i++)
            {
                float n = i / (float)(c.Samples - 1);
                times[i] = n * durSec;
                intensities[i] = Mathf.Clamp01(c.Intensity.Evaluate(n));
            }
            times[0] = 0f;
            times[c.Samples - 1] = durSec;
        }
    }
}
#endif
