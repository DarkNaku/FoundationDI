using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class NativePolicyAgreementPromptTest
{
    private static readonly PolicyAgreementContent Content =
        new("Notice", "당사의 {terms}과 {privacy}", "OK", "이용약관", "https://t", "개인정보 처리방침", "https://p");

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 링크가_걸린_본문으로_네이티브_창을_띄우고_동의하면_완료된다() => AwaitableTest.Run(async () =>
    {
        var dialog = new FakeNativePolicyDialog();
        var sut = new NativePolicyAgreementPrompt(dialog, new FakePolicyPrompt(new List<string>()));

        var showing = sut.ShowAsync(Content);

        Assert.AreEqual(1, dialog.ShowCount);
        Assert.AreEqual("Notice", dialog.LastContent.Title);
        Assert.AreEqual("당사의 이용약관과 개인정보 처리방침", dialog.LastMessage.Text);
        Assert.AreEqual(2, dialog.LastMessage.Links.Count);

        dialog.Accept();
        await showing;
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 네이티브_창을_못_띄우면_대체_창을_쓴다() => AwaitableTest.Run(async () =>
    {
        var dialog = new FakeNativePolicyDialog { IsSupported = false };
        var fallback = new FakePolicyPrompt(new List<string>());
        var sut = new NativePolicyAgreementPrompt(dialog, fallback);

        var showing = sut.ShowAsync(Content);

        Assert.AreEqual(0, dialog.ShowCount);
        Assert.AreEqual(1, fallback.ShowCount);

        fallback.Accept();
        await showing;
    });

    [Test]
    public void 닫으면_네이티브_창을_정리하고_늦게_온_동의는_무시한다()
    {
        var dialog = new FakeNativePolicyDialog();
        var sut = new NativePolicyAgreementPrompt(dialog, new FakePolicyPrompt(new List<string>()));

        var showing = sut.ShowAsync(Content);
        sut.Close();
        dialog.Accept();

        Assert.AreEqual(1, dialog.CloseCount);
        Assert.IsFalse(showing.IsCompleted, "Close는 ShowAsync를 완료시키지 않는다(IPolicyAgreementPrompt 계약)");
    }
}
