using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdProviderFactoryTest
{
    [Test]
    public void SDK_심볼이_없는_provider를_요청하면_경고와_함께_Dummy로_폴백한다()
    {
        var effective = AdProviderFactory.Resolve(AdProviderType.AdMob, forceDummy: false, out var warning);

        // 이 리포지토리에는 아직 어떤 광고 SDK도 설치되어 있지 않다.
        Assert.AreEqual(AdProviderType.Dummy, effective);
        Assert.IsNotNull(warning, "폴백했는데 경고가 없다");
        StringAssert.Contains("AdMob", warning);
    }

    [Test]
    public void Dummy를_요청하면_경고_없이_Dummy를_쓴다()
    {
        var effective = AdProviderFactory.Resolve(AdProviderType.Dummy, forceDummy: false, out var warning);

        Assert.AreEqual(AdProviderType.Dummy, effective);
        Assert.IsNull(warning);
    }

    [Test]
    public void 강제_더미가_켜지면_요청과_무관하게_Dummy를_쓰고_경고하지_않는다()
    {
        // 에디터 강제 더미는 의도된 설정이므로 경고를 띄우면 매 실행마다 소음이 된다.
        var effective = AdProviderFactory.Resolve(AdProviderType.LevelPlay, forceDummy: true, out var warning);

        Assert.AreEqual(AdProviderType.Dummy, effective);
        Assert.IsNull(warning);
    }

    [Test]
    public void 설정은_인스펙터_값을_그대로_서비스_옵션으로_옮긴다()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<AdServiceSettings>();

        var options = settings.ToOptions();

        // 기본값이 스펙과 일치하는지 확인한다. 여기가 어긋나면 재시도 동작이 조용히 달라진다.
        Assert.AreEqual(5, options.RetryPolicy.MaxAttempts);
        Assert.AreEqual(2f, options.RetryPolicy.BaseSeconds, 0.001f);
        Assert.AreEqual(64f, options.RetryPolicy.MaxDelaySeconds, 0.001f);
        Assert.AreEqual(1, options.RewardGraceFrames);
        Assert.IsTrue(options.AutoLoadOnInitialize);
        Assert.AreEqual(BannerPosition.Bottom, options.BannerOptions.Position);

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }
}
