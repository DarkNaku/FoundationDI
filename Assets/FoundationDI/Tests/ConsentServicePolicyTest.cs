using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class ConsentServicePolicyTest
{
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 수락하지_않았으면_약관_창을_띄우고_수락할_때까지_기다린다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        var requesting = sut.RequestAsync();

        Assert.IsTrue(f.Prompt.IsShowing, "약관 창을 띄우지 않았다");
        Assert.AreEqual(0, f.Storage.SaveCount, "수락 전에 저장했다");
        Assert.AreEqual(0, f.Ump.UpdateCount, "약관 수락 전에 UMP를 시작했다");

        f.Prompt.Accept();
        var result = await requesting;

        Assert.IsTrue(result.PolicyAccepted);
        Assert.AreEqual(1, f.Storage.Version, "수락한 버전을 저장하지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_수락한_버전이면_창을_띄우지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Prompt.ShowCount);
        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_버전이_오르면_다시_묻는다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture { PolicyVersion = 2 };
        f.Storage.Version = 1;
        var sut = f.Build();

        var requesting = sut.RequestAsync();

        Assert.IsTrue(f.Prompt.IsShowing, "약관이 바뀌었는데 다시 묻지 않았다");

        f.Prompt.Accept();
        await requesting;

        Assert.AreEqual(2, f.Storage.Version);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_버전이_0이면_약관_단계를_쓰지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture { PolicyVersion = 0 };

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Prompt.ShowCount);
        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator AcceptPolicy를_부르면_창_없이_수락으로_본다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        sut.AcceptPolicy();
        var result = await sut.RequestAsync();

        Assert.AreEqual(0, f.Prompt.ShowCount, "기존 유저에게 약관 창을 띄웠다");
        Assert.IsTrue(result.PolicyAccepted);
        Assert.IsTrue(sut.IsPolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator SetPolicyContent로_바꾼_문구를_창에_넘긴다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();
        var localized = new PolicyAgreementContent("Terms", "Body", "Agree", "ToS", "https://t", "Privacy", "https://p");

        sut.SetPolicyContent(localized);
        var requesting = sut.RequestAsync();

        Assert.AreEqual("Terms", f.Prompt.LastContent.Title);

        f.Prompt.Accept();
        await requesting;
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_창이_예외를_던지면_수락하지_않음으로_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        f.Prompt.Throws = true;

        LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("ConsentService"));

        var result = await f.Build().RequestAsync();

        Assert.IsFalse(result.PolicyAccepted, "보지도 않은 약관을 수락한 것으로 처리했다");
        Assert.IsFalse(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Unknown, result.AnalyticsConsent);
        Assert.AreEqual(0, f.Storage.SaveCount);
    });
}
