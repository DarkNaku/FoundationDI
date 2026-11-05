using DarkNaku.FoundationDI;
using NUnit.Framework;

public class PolicyMessageFormatterTest
{
    private static PolicyAgreementContent Content(string message, string termsUrl = "https://t", string privacyUrl = "https://p") =>
        new("Notice", message, "OK", "이용약관", termsUrl, "개인정보 처리방침", privacyUrl);

    private static string Linked(PolicyMessage message, int index) =>
        message.Text.Substring(message.Links[index].Start, message.Links[index].Length);

    [Test]
    public void 자리표시자를_라벨로_바꾸고_그_자리에_링크를_건다()
    {
        var message = PolicyMessageFormatter.Format(Content("당사의 {terms}과 {privacy}에 동의합니다."));

        Assert.AreEqual("당사의 이용약관과 개인정보 처리방침에 동의합니다.", message.Text);
        Assert.AreEqual(2, message.Links.Count);
        Assert.AreEqual("이용약관", Linked(message, 0));
        Assert.AreEqual("https://t", message.Links[0].Url);
        Assert.AreEqual("개인정보 처리방침", Linked(message, 1));
        Assert.AreEqual("https://p", message.Links[1].Url);
    }

    [Test]
    public void 자리표시자가_없으면_본문에_있는_라벨_첫_위치에_링크를_건다()
    {
        const string text = "\"OK\" 버튼을 누르시면 당사의 이용약관과 개인정보 처리방침에 대해 읽고 확인한 것으로 간주합니다.";
        var message = PolicyMessageFormatter.Format(Content(text));

        Assert.AreEqual(text, message.Text);
        Assert.AreEqual("이용약관", Linked(message, 0));
        Assert.AreEqual("개인정보 처리방침", Linked(message, 1));
    }

    [Test]
    public void 본문에_라벨이_없으면_줄을_바꿔_붙이고_링크를_건다()
    {
        var message = PolicyMessageFormatter.Format(Content("게임을 시작하려면 동의해 주세요."));

        Assert.AreEqual("게임을 시작하려면 동의해 주세요.\n\n이용약관\n개인정보 처리방침", message.Text);
        Assert.AreEqual("이용약관", Linked(message, 0));
        Assert.AreEqual("개인정보 처리방침", Linked(message, 1));
    }

    [Test]
    public void URL이_비어_있으면_라벨만_넣고_링크를_걸지_않는다()
    {
        var message = PolicyMessageFormatter.Format(Content("{terms}과 {privacy}", termsUrl: ""));

        Assert.AreEqual("이용약관과 개인정보 처리방침", message.Text);
        Assert.AreEqual(1, message.Links.Count);
        Assert.AreEqual("개인정보 처리방침", Linked(message, 0));
    }
}
