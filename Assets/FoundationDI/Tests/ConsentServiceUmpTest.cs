using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class ConsentServiceUmpTest
{
    private static ConsentFixture Accepted()
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;
        return f;
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_다음에_선행_작업을_끝내고_UMP를_시작한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var prerequisite = new FakeConsentPrerequisite(f.Log) { Defer = true };
        f.Prerequisites.Add(prerequisite);
        var sut = f.Build();

        var requesting = sut.RequestAsync();
        f.Prompt.Accept();

        Assert.IsTrue(prerequisite.IsPending);
        Assert.AreEqual(0, f.Ump.UpdateCount, "선행 작업(Firebase 초기화)이 끝나기 전에 UMP를 시작했다");

        prerequisite.Complete();
        await requesting;

        CollectionAssert.AreEqual(new[] { "policy", "prereq", "ump.update", "ump.form" }, f.Log);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 요청_옵션을_UMP에_넘긴다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.UmpRequest = new UmpRequestOptions(UmpDebugGeography.Eea, new List<string> { "hash" }, true);

        await f.Build().RequestAsync();

        Assert.AreEqual(UmpDebugGeography.Eea, f.Ump.LastOptions.DebugGeography);
        Assert.AreEqual("hash", f.Ump.LastOptions.TestDeviceHashedIds[0]);
        Assert.IsTrue(f.Ump.LastOptions.TagForUnderAgeOfConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 폼에서_동의한_결과를_돌려준다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.Status = UmpConsentStatus.Required;
        f.Ump.CanRequestAds = false;
        f.Ump.OnForm = () =>
        {
            f.Ump.Status = UmpConsentStatus.Obtained;
            f.Ump.CanRequestAds = true;
            f.Ump.Tcf = new TcfSignals(true, "1111111111");
        };

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
        Assert.AreEqual(true, result.Tcf.GdprApplies);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 폼에서_거부해도_게임은_진행되고_분석은_거부가_된다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.OnForm = () =>
        {
            f.Ump.Status = UmpConsentStatus.Obtained;
            f.Ump.CanRequestAds = true;   // 거부해도 비개인화 광고는 요청할 수 있다
            f.Ump.Tcf = new TcfSignals(true, "0000000000");
        };

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Denied, result.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP를_끄면_호출하지_않고_허용으로_본다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.UseUmp = false;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Ump.UpdateCount);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_네이티브가_없으면_허용으로_본다() => AwaitableTest.Run(async () =>
    {
        // 에디터와 포드·aar가 빠진 빌드. 막으면 에디터에서 광고 테스트가 안 된다.
        var f = Accepted();
        f.Ump.IsSupported = false;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Ump.UpdateCount);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 선행_작업은_세션에_한_번만_돈다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var prerequisite = new FakeConsentPrerequisite(f.Log);
        f.Prerequisites.Add(prerequisite);
        f.Ump.CanRequestAds = false;   // 확정되지 않아 두 번째 요청이 다시 돈다
        var sut = f.Build();

        await sut.RequestAsync();
        await sut.RequestAsync();

        Assert.AreEqual(1, prerequisite.PrepareCount);
        Assert.AreEqual(2, f.Ump.UpdateCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 선행_작업이_실패해도_UMP는_진행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Prerequisites.Add(new FakeConsentPrerequisite(f.Log) { Throws = true });

        LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("선행 작업"));

        await f.Build().RequestAsync();

        Assert.AreEqual(1, f.Ump.UpdateCount);
    });
}
