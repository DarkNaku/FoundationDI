using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class PolicyContentSelectorTest
{
    private static readonly PolicyAgreementContent Default =
        new("Notice", "By tapping OK you agree to our {terms} and {privacy}.", "OK",
            "Terms of Service", "https://t", "Privacy Policy", "https://p");

    private static readonly LocalizedPolicyText Korean =
        new(SystemLanguage.Korean, "알림", "\"OK\" 버튼을 누르시면 당사의 {terms}과 {privacy}에 동의합니다.", "OK",
            "이용약관", "", "개인정보 처리방침", "");

    private static readonly LocalizedPolicyText Japanese =
        new(SystemLanguage.Japanese, "お知らせ", "{terms}と{privacy}に同意します。", "OK",
            "利用規約", "https://t/ja", "プライバシーポリシー", "https://p/ja");

    [Test]
    public void 기기_언어와_같은_항목의_문구를_쓴다()
    {
        var content = PolicyContentSelector.Select(Default, new List<LocalizedPolicyText> { Korean, Japanese },
                                                   SystemLanguage.Japanese);

        Assert.AreEqual("お知らせ", content.Title);
        Assert.AreEqual("{terms}と{privacy}に同意します。", content.Message);
        Assert.AreEqual("利用規約", content.TermsLabel);
        Assert.AreEqual("https://t/ja", content.TermsUrl);
    }

    [Test]
    public void 맞는_언어가_없으면_기본_문구를_쓴다()
    {
        var content = PolicyContentSelector.Select(Default, new List<LocalizedPolicyText> { Korean },
                                                   SystemLanguage.French);

        Assert.AreEqual("Notice", content.Title);
        Assert.AreEqual("Terms of Service", content.TermsLabel);
    }

    [Test]
    public void 항목의_빈_칸은_기본값으로_채운다()
    {
        var content = PolicyContentSelector.Select(Default, new List<LocalizedPolicyText> { Korean },
                                                   SystemLanguage.Korean);

        Assert.AreEqual("이용약관", content.TermsLabel);
        Assert.AreEqual("https://t", content.TermsUrl, "언어마다 URL이 같으면 비워 두면 된다");
        Assert.AreEqual("https://p", content.PrivacyUrl);
    }

    [TestCase(SystemLanguage.ChineseSimplified, SystemLanguage.Chinese)]
    [TestCase(SystemLanguage.ChineseTraditional, SystemLanguage.Chinese)]
    [TestCase(SystemLanguage.Chinese, SystemLanguage.ChineseSimplified)]
    public void 중국어는_간체_번체_통합_항목끼리_서로_대신한다(SystemLanguage device, SystemLanguage entry)
    {
        var chinese = new LocalizedPolicyText(entry, "通知", "", "", "", "", "", "");

        var content = PolicyContentSelector.Select(Default, new List<LocalizedPolicyText> { chinese }, device);

        Assert.AreEqual("通知", content.Title);
    }

    [Test]
    public void 중국어는_정확히_맞는_항목을_먼저_쓴다()
    {
        var simplified = new LocalizedPolicyText(SystemLanguage.ChineseSimplified, "通知", "", "", "", "", "", "");
        var traditional = new LocalizedPolicyText(SystemLanguage.ChineseTraditional, "通知(繁)", "", "", "", "", "", "");

        var content = PolicyContentSelector.Select(Default, new List<LocalizedPolicyText> { simplified, traditional },
                                                   SystemLanguage.ChineseTraditional);

        Assert.AreEqual("通知(繁)", content.Title);
    }
}
