using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class HapticDataTest
{
    [Test]
    public void HapticCurve_편의생성자는_양_플랫폼_구조체를_기본값으로_채운다()
    {
        var intensity = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var hc = new HapticCurve(intensity, durationMs: 200f, sharpness: 0.4f, samples: 8,
                                 fallback: HapticImpact.Heavy, delayMs: 10f, androidMaxAmplitude: 200);

        Assert.AreSame(intensity, hc.IOS.Intensity);
        Assert.AreEqual(200f, hc.IOS.DurationMs);
        Assert.AreEqual(0.4f, hc.IOS.Sharpness);
        Assert.AreEqual(8, hc.IOS.Samples);
        Assert.AreEqual(10f, hc.IOS.DelayMs);
        Assert.AreEqual(HapticImpact.Heavy, hc.IOS.Fallback);

        Assert.AreSame(intensity, hc.Android.Intensity);
        Assert.AreEqual(200L, hc.Android.DurationMs);
        Assert.AreEqual(200, hc.Android.MaxAmplitude);
        Assert.AreEqual(8, hc.Android.Samples);
        Assert.AreEqual(10L, hc.Android.DelayMs);
        Assert.AreEqual(HapticImpact.Heavy, hc.Android.Fallback);
    }

    [Test]
    public void HapticPattern_생성자는_양_플랫폼_배열을_보관한다()
    {
        var ios = new[] { new iOSPulse { Preset = HapticPreset.Selection, DelayMs = 0f } };
        var android = new[] { new AndroidPulse { DelayMs = 0L, PulseMs = 50L, Amplitude = 180 } };

        var p = new HapticPattern(ios, android);

        Assert.AreSame(ios, p.IOS);
        Assert.AreSame(android, p.Android);
    }
}
