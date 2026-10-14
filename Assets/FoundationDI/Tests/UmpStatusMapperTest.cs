using DarkNaku.FoundationDI;
using NUnit.Framework;

public class UmpStatusMapperTest
{
    [TestCase(0, UmpConsentStatus.Unknown)]
    [TestCase(1, UmpConsentStatus.NotRequired)]
    [TestCase(2, UmpConsentStatus.Required)]
    [TestCase(3, UmpConsentStatus.Obtained)]
    [TestCase(99, UmpConsentStatus.Unknown)]
    public void 안드로이드_상태를_번역한다(int native, UmpConsentStatus expected)
    {
        Assert.AreEqual(expected, UmpStatusMapper.FromAndroid(native));
    }

    [TestCase(0, UmpConsentStatus.Unknown)]
    [TestCase(1, UmpConsentStatus.Required)]
    [TestCase(2, UmpConsentStatus.NotRequired)]
    [TestCase(3, UmpConsentStatus.Obtained)]
    [TestCase(99, UmpConsentStatus.Unknown)]
    public void iOS_상태를_번역한다(int native, UmpConsentStatus expected)
    {
        // iOS는 Required와 NotRequired의 순서가 안드로이드와 반대다. 캐스팅하면 조용히 뒤집힌다.
        Assert.AreEqual(expected, UmpStatusMapper.FromIos(native));
    }

    [TestCase(UmpDebugGeography.Disabled, 0)]
    [TestCase(UmpDebugGeography.Eea, 1)]
    [TestCase(UmpDebugGeography.NotEea, 2)]
    [TestCase(UmpDebugGeography.RegulatedUsState, 3)]
    [TestCase(UmpDebugGeography.Other, 4)]
    public void 디버그_지역을_네이티브_값으로_번역한다(UmpDebugGeography geography, int expected)
    {
        Assert.AreEqual(expected, UmpStatusMapper.ToAndroidGeography(geography));
        Assert.AreEqual(expected, UmpStatusMapper.ToIosGeography(geography));
    }
}
