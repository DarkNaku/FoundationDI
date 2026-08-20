using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

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
        Assert.AreEqual(120f, options.InterstitialCooldownSeconds, 0.001f,
                        "전면 쿨다운 기본값은 120초여야 한다");

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }

    [Test]
    public void 전면_쿨다운_설정값이_음수여도_ToOptions는_0으로_클램프한다()
    {
        // [Min(0)]은 인스펙터 편집만 막는다 — 손으로 고친 .asset의 음수 값은 그대로 들어온다.
        // 음수 쿨다운이 새어나오면 Delay(음수, ...)가 즉시(또는 과거로) 발화해 게이트가 무력화된다.
        var settings = UnityEngine.ScriptableObject.CreateInstance<AdServiceSettings>();
        UnityEngine.JsonUtility.FromJsonOverwrite("{\"_interstitialCooldownSeconds\":-30}", settings);

        var options = settings.ToOptions();

        Assert.AreEqual(0f, options.InterstitialCooldownSeconds, 0.001f,
                        "음수 쿨다운이 0으로 클램프되지 않았다");

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }

    [Test]
    public void 설정의_광고단위ID가_포맷별로_섞이지_않고_각자의_슬롯에_들어간다()
    {
        // interstitial:/rewarded: 인자가 뒤바뀌어도 숫자 기본값만 보는 테스트는 여전히
        // 통과한다 — 실기에서는 전면이 보상 유닛 ID를 요청하고 반대도 마찬가지가 되어
        // 필당·수익 귀속이 깨진다. 세 슬롯에 서로 다른 값을 넣고 각자 제자리에 있는지 본다.
        var settings = UnityEngine.ScriptableObject.CreateInstance<AdServiceSettings>();

        var json = "{" +
                   "\"_bannerUnitId\":{\"_android\":\"ban-a\",\"_ios\":\"ban-i\"}," +
                   "\"_interstitialUnitId\":{\"_android\":\"int-a\",\"_ios\":\"int-i\"}," +
                   "\"_rewardedUnitId\":{\"_android\":\"rew-a\",\"_ios\":\"rew-i\"}" +
                   "}";
        UnityEngine.JsonUtility.FromJsonOverwrite(json, settings);

        var options = settings.ToOptions();

        Assert.AreEqual("ban-a", options.Banner.Android);
        Assert.AreEqual("ban-i", options.Banner.iOS);
        Assert.AreEqual("int-a", options.Interstitial.Android);
        Assert.AreEqual("int-i", options.Interstitial.iOS);
        Assert.AreEqual("rew-a", options.Rewarded.Android);
        Assert.AreEqual("rew-i", options.Rewarded.iOS);

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }

    [Test]
    public void 재시도_설정값이_0이나_음수여도_ToOptions는_붕괴하지_않는_값으로_클램프한다()
    {
        // [Min] 어트리뷰트는 인스펙터 편집만 막는다 — 스크립트로 만들었거나 손으로 고친
        // .asset은 그대로 통과한다. retryBaseSeconds=0이면 Mathf.Pow(0, n)이 항상 0이라
        // 지수 백오프가 즉시재시도 5연발로 무너지고, maxRetryDelaySeconds가 음수면
        // Mathf.Min이 모든 지연을 그 음수로 눌러버린다.
        var settings = UnityEngine.ScriptableObject.CreateInstance<AdServiceSettings>();

        var json = "{\"_maxRetryAttempts\":-3,\"_retryBaseSeconds\":0,\"_maxRetryDelaySeconds\":-5}";
        UnityEngine.JsonUtility.FromJsonOverwrite(json, settings);

        var options = settings.ToOptions();

        Assert.GreaterOrEqual(options.RetryPolicy.MaxAttempts, 0, "음수 시도 횟수가 그대로 새어나왔다");
        // base는 1 이하로 클램프되면 안 된다 — Pow(base, attempt)가 attempt에 따라
        // 자라려면 base > 1이어야 한다(1이면 항상 1, 1 미만이면 오히려 줄어든다).
        Assert.Greater(options.RetryPolicy.BaseSeconds, 1f, "0인 base가 1 이하로 클램프돼 백오프가 자라지 않는다");
        Assert.Greater(options.RetryPolicy.MaxDelaySeconds, 0f, "음수 상한이 그대로 새어나왔다");

        // 클램프된 값으로 실제 지연을 계산해도 0이나 음수가 나오면 안 된다.
        Assert.Greater(options.RetryPolicy.DelayFor(1), 0f);

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }

    [Test]
    public void 재시도_밑값이_0이어도_ToOptions는_시도할수록_지연이_실제로_늘어나는_값으로_클램프한다()
    {
        // 위 테스트는 "0이나 음수가 새어나오지 않는다"만 본다 — base가 1 이하(예: 옛
        // 클램프 0.1)로만 눌러도 그 assert는 통과해버린다. 여기서는 그게 진짜 백오프인지,
        // 즉 시도할수록 지연이 커지는지를 직접 확인한다. maxRetryDelaySeconds는 손대지
        // 않고 기본값(64초)을 둔다 — 함께 아주 작은 값으로 눌러버리면 Mathf.Min 상한
        // 자체가 병목이 되어 base가 아무리 커도 지연이 그 상한에서 묶여버려 성장을
        // 관찰할 수 없다(위 테스트의 -5 조합이 그렇다).
        var settings = UnityEngine.ScriptableObject.CreateInstance<AdServiceSettings>();
        UnityEngine.JsonUtility.FromJsonOverwrite("{\"_retryBaseSeconds\":0}", settings);

        var policy = settings.ToOptions().RetryPolicy;

        Assert.Greater(policy.BaseSeconds, 1f, "밑값이 1 이하로 클램프돼 백오프가 자라지 않는다");
        Assert.Greater(policy.DelayFor(2), policy.DelayFor(1), "시도 2회차 지연이 1회차보다 커야 한다");
        Assert.Greater(policy.DelayFor(3), policy.DelayFor(2), "시도 3회차 지연이 2회차보다 커야 한다");

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }

    [Test]
    public void Create는_Resolve가_고른_effective를_실제로_소비해서_Dummy_provider를_반환한다()
    {
        // Create가 effective를 무시하고 항상 DummyAdProvider를 새로 만들기만 해도 이 값 자체는
        // 우연히 통과한다. 그래서 아래 경고 로그 테스트와 짝을 이뤄야 "Resolve의 결과가 실제로
        // Create의 분기에 쓰였다"는 것을 검증한다.
        var factory = new AdProviderFactory(new FakeAdDispatcher());

        var provider = factory.Create(AdProviderType.Dummy, DummyAdOptions.Default, forceDummy: false);

        Assert.IsInstanceOf<DummyAdProvider>(provider);
    }

    [Test]
    public void Create는_폴백이_발생하면_Resolve와_같은_경고를_로그로_남긴다()
    {
        var factory = new AdProviderFactory(new FakeAdDispatcher());

        LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("AdMob"));
        var provider = factory.Create(AdProviderType.AdMob, DummyAdOptions.Default, forceDummy: false);

        Assert.IsInstanceOf<DummyAdProvider>(provider);
    }

    [Test]
    public void Build은_사용가능하다고_판단됐지만_구현되지_않은_타입이면_에러를_로그로_남기고_Dummy로_대체한다()
    {
        // Create를 거쳐서는 이 상태(effective가 Dummy가 아닌 채로 Build에 들어옴)에 닿을 수
        // 없다 — Resolve가 심볼 없이는 Dummy 외의 값을 절대 돌려주지 않기 때문이다. Build를
        // internal로 분리해 "이미 사용 가능하다고 판단된" 상태를 직접 주입해 default 분기
        // (에러 로그 + Dummy 대체)를 검증한다.
        var factory = new AdProviderFactory(new FakeAdDispatcher());

        LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("AdMob"));
        var provider = factory.Build(AdProviderType.AdMob, DummyAdOptions.Default);

        Assert.IsInstanceOf<DummyAdProvider>(provider);
    }
}
