using DarkNaku.FoundationDI;
using NUnit.Framework;

public class ConsentResultTest
{
    private static ConsentResult Result(bool policyAccepted, bool canRequestAds, UmpConsentStatus umpStatus,
                                        TcfSignals tcf = default) =>
        new(policyAccepted, canRequestAds, AnalyticsConsent.Unknown, TrackingAuthorizationStatus.NotDetermined,
            tcf, umpStatus);

    [Test]
    public void 약관을_수락했고_UMP_상태가_Unknown이며_광고를_요청할_수_없으면_판정_미완료다()
    {
        Assert.IsTrue(Result(true, false, UmpConsentStatus.Unknown, TcfSignals.None).IsAdConsentUndetermined,
                      "첫 실행에서 UMP 갱신이 실패한 유저를 답변하지 않은 유저와 구분하지 못한다");
    }

    [Test]
    public void GDPR_적용_신호가_있으면_판정_미완료가_아니다()
    {
        var eea = Result(true, false, UmpConsentStatus.Unknown, new TcfSignals(true, null));

        Assert.IsFalse(eea.IsAdConsentUndetermined, "유럽 유저에게 동의 없이 광고를 요청하게 된다");
    }

    [Test]
    public void 답변이_필요한_상태는_판정_미완료가_아니다()
    {
        Assert.IsFalse(Result(true, false, UmpConsentStatus.Required, TcfSignals.None).IsAdConsentUndetermined,
                       "동의 창에 답하지 않은 유저에게 광고를 요청하게 된다");
    }

    [Test]
    public void 약관을_수락하지_않았으면_판정_미완료가_아니다()
    {
        Assert.IsFalse(Result(false, false, UmpConsentStatus.Unknown, TcfSignals.None).IsAdConsentUndetermined);
    }
}
