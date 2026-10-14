using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AnalyticsConsentJudgeTest
{
    [Test]
    public void 규제_대상이_아니면_허용한다()
    {
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.NotRequired, TcfSignals.None));
    }

    [Test]
    public void GDPR_지역에서_목적1에_동의하면_허용한다()
    {
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, new TcfSignals(true, "1000000000")));
    }

    [Test]
    public void GDPR_지역에서_목적1을_거부하면_거부한다()
    {
        Assert.AreEqual(AnalyticsConsent.Denied,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, new TcfSignals(true, "0111111111")));
    }

    [Test]
    public void GDPR_지역인데_목적_문자열이_없으면_아직_모른다()
    {
        // 폼이 아직 안 떴거나 실패한 경우다. 거부로 보면 버퍼를 버리게 되는데 사용자는 거부한 적이 없다.
        Assert.AreEqual(AnalyticsConsent.Unknown,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Required, new TcfSignals(true, null)));
    }

    [Test]
    public void GDPR이_적용되지_않으면_허용한다()
    {
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, new TcfSignals(false, null)));
    }

    [Test]
    public void 판정_정보가_없으면_아직_모른다()
    {
        // 첫 실행 오프라인: 상태 Unknown, TCF 키 없음.
        Assert.AreEqual(AnalyticsConsent.Unknown,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Unknown, TcfSignals.None));
    }

    [Test]
    public void 동의를_받았는데_GDPR_키가_없으면_허용한다()
    {
        // 미국 주 규정 메시지만 쓰는 경우 등. 옵트아웃 체계라 분석은 기본 허용이다.
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, TcfSignals.None));
    }
}
