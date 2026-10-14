using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ConsentServiceRobustnessTest
{
    private static ConsentFixture Accepted()
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;
        return f;
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_실패해도_이전_값으로_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Obtained;
        f.Ump.Tcf = new TcfSignals(true, "1111111111");

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신이 실패"));

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Ump.FormCount, "갱신 실패 뒤 폼을 띄우려 했다");
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent, "캐시된 이전 세션 값을 쓰지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_예외를_던져도_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.ThrowOnUpdate = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신 중 예외"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_시간을_넘기면_실패로_보고_진행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.DeferUpdate = true;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var requesting = f.Build().RequestAsync();

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("초 안에 끝나지 않았다"));

        f.Dispatcher.Advance(f.UpdateTimeout);
        var result = await requesting;

        Assert.AreEqual(0, f.Ump.FormCount);
        Assert.AreEqual(AnalyticsConsent.Unknown, result.AnalyticsConsent, "첫 실행 오프라인은 판정 불가여야 한다");

        f.Ump.CompleteUpdate(true);   // 늦게 도착한 결과는 무시된다(예외 없이)
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 폼이_예외를_던져도_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.ThrowOnForm = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("폼 표시 중 예외"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 네이티브_읽기가_던져도_결과를_만든다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.ThrowOnRead = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("읽지 못했다"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 진행_중에_다시_부르면_같은_흐름에_편승한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        var first = sut.RequestAsync();
        var second = sut.RequestAsync();

        Assert.AreEqual(1, f.Prompt.ShowCount, "약관 창을 두 번 띄웠다");

        f.Prompt.Accept();

        Assert.IsTrue((await first).PolicyAccepted);
        Assert.IsTrue((await second).PolicyAccepted);
        Assert.AreEqual(1, f.Ump.UpdateCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정된_결과가_있으면_다시_묻지_않고_돌려준다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var sut = f.Build();

        await sut.RequestAsync();
        var again = await sut.RequestAsync();

        Assert.AreEqual(1, f.Ump.UpdateCount, "광고·분석 초기화가 각자 부를 때마다 네트워크 왕복이 생긴다");
        Assert.IsTrue(again.CanRequestAds);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정되지_않았으면_다시_부를_때_UMP만_재판정한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var sut = f.Build();

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신이 실패"));
        Assert.AreEqual(AnalyticsConsent.Unknown, (await sut.RequestAsync()).AnalyticsConsent);

        f.Ump.UpdateResult = true;
        f.Ump.Status = UmpConsentStatus.NotRequired;
        var second = await sut.RequestAsync();

        Assert.AreEqual(2, f.Ump.UpdateCount);
        Assert.AreEqual(0, f.Prompt.ShowCount, "재판정에서 약관을 다시 물었다");
        Assert.AreEqual(AnalyticsConsent.Granted, second.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 재판정_결과가_바뀌면_ConsentChanged를_발행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var sut = f.Build();
        var changed = 0;
        sut.ConsentChanged += () => changed++;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신이 실패"));
        await sut.RequestAsync();

        Assert.AreEqual(0, changed, "첫 완료는 변경이 아니다");

        f.Ump.UpdateResult = true;
        f.Ump.Status = UmpConsentStatus.NotRequired;
        await sut.RequestAsync();

        Assert.AreEqual(1, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 개인정보_옵션_폼이_닫히면_ConsentChanged를_발행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.IsPrivacyOptionsRequired = true;
        f.Ump.OnPrivacyOptions = () => f.Ump.Tcf = new TcfSignals(true, "0000000000");
        f.Ump.Status = UmpConsentStatus.Obtained;
        var sut = f.Build();
        var changed = 0;
        sut.ConsentChanged += () => changed++;

        Assert.IsTrue(sut.IsPrivacyOptionsRequired);

        await sut.ShowPrivacyOptionsAsync();

        Assert.AreEqual(1, f.Ump.PrivacyOptionsCount);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(AnalyticsConsent.Denied, sut.Current.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_창이_떠_있을_때_Dispose하면_창을_닫고_수락하지_않음으로_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        var requesting = sut.RequestAsync();
        sut.Dispose();
        var result = await requesting;

        Assert.AreEqual(1, f.Prompt.CloseCount, "창을 정리하지 않았다");
        Assert.IsFalse(result.PolicyAccepted);
        Assert.AreEqual(0, f.Storage.SaveCount);

        f.Prompt.Accept();   // Dispose 뒤 늦게 눌려도 저장하지 않는다
        Assert.AreEqual(0, f.Storage.SaveCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose_뒤의_요청은_즉시_현재_상태로_끝난다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var sut = f.Build();

        sut.Dispose();
        var result = await sut.RequestAsync();

        Assert.AreEqual(0, f.Ump.UpdateCount);
        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_폼이_떠_있을_때_Dispose해도_대기자는_완료된다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.DeferForm = true;
        var sut = f.Build();

        var requesting = sut.RequestAsync();

        Assert.IsTrue(f.Ump.IsFormPending);

        sut.Dispose();
        await requesting;

        f.Ump.CompleteForm();   // 늦은 콜백이 예외를 내지 않는다
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_실패하면_물러나며_다시_판정한다() => AwaitableTest.Run(async () =>
    {
        // 첫 실행 오프라인. 게임이 RequestAsync를 다시 부르지 않아도 판정이 나야 분석 버퍼가 풀리고
        // 비 EEA 사용자의 광고가 세션 내내 죽지 않는다.
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var sut = f.Build();
        var changed = 0;
        sut.ConsentChanged += () => changed++;

        await sut.RequestAsync();
        Assert.AreEqual(1, f.Ump.UpdateCount);

        f.Dispatcher.Advance(ConsentService.RetryDelaySeconds[0]);
        Assert.AreEqual(2, f.Ump.UpdateCount, "실패 뒤 다시 판정하지 않았다");

        f.Ump.UpdateResult = true;
        f.Ump.Status = UmpConsentStatus.NotRequired;
        f.Dispatcher.Advance(ConsentService.RetryDelaySeconds[1]);

        Assert.AreEqual(3, f.Ump.UpdateCount, "간격이 늘어난 두 번째 재시도가 없었다");
        Assert.AreEqual(1, changed);
        Assert.AreEqual(AnalyticsConsent.Granted, sut.Current.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정되면_더_이상_재판정하지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var sut = f.Build();

        await sut.RequestAsync();
        f.Dispatcher.Advance(3600f);

        Assert.AreEqual(1, f.Ump.UpdateCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_재판정을_멈춘다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var sut = f.Build();

        await sut.RequestAsync();
        sut.Dispose();
        f.Dispatcher.Advance(3600f);

        Assert.AreEqual(1, f.Ump.UpdateCount);
    });
}
