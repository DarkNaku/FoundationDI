using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public struct iOSHapticCurve
    {
        public AnimationCurve Intensity; // X:0..1(정규화 시간), Y:0..1(세기)
        public float DurationMs;
        public float Sharpness;          // 0..1 (CoreHaptics)
        public int Samples;              // 컨트롤 포인트 2..64
        public float DelayMs;
        public HapticImpact Fallback;    // CoreHaptics 미지원 → 프리셋 폴백
    }

    public struct AndroidHapticCurve
    {
        public AnimationCurve Intensity;
        public long DurationMs;
        public int MaxAmplitude;         // 1..255
        public int Samples;              // waveform 세그먼트 2..64
        public long DelayMs;
        public HapticImpact Fallback;    // 진폭제어 미지원 → 프리셋 폴백
    }

    public struct HapticCurve
    {
        public iOSHapticCurve IOS;
        public AndroidHapticCurve Android;

        // 간단: 곡선 하나 + 기본값 → 양 플랫폼 동시 세팅
        public HapticCurve(AnimationCurve intensity, float durationMs = 160f, float sharpness = 0.6f,
                           int samples = 16, HapticImpact fallback = HapticImpact.Medium,
                           float delayMs = 0f, int androidMaxAmplitude = 255)
        {
            IOS = new iOSHapticCurve
            {
                Intensity = intensity,
                DurationMs = durationMs,
                Sharpness = sharpness,
                Samples = samples,
                DelayMs = delayMs,
                Fallback = fallback
            };
            Android = new AndroidHapticCurve
            {
                Intensity = intensity,
                DurationMs = (long)durationMs,
                MaxAmplitude = androidMaxAmplitude,
                Samples = samples,
                DelayMs = (long)delayMs,
                Fallback = fallback
            };
        }

        // 정밀: 각 플랫폼 독립 캘리브레이션
        public HapticCurve(iOSHapticCurve ios, AndroidHapticCurve android)
        {
            IOS = ios;
            Android = android;
        }
    }
}
