using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class PolicyAgreementPromptTest
{
    private class RecordingOpener : IUrlOpener
    {
        public readonly List<string> Opened = new();
        public void Open(string url) => Opened.Add(url);
    }

    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();

        foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(es.gameObject);
    }

    // 프리팹 대신 같은 모양의 계층을 코드로 만든다(테스트가 에셋에 의존하지 않게).
    private PolicyAgreementView CreateViewTemplate()
    {
        var root = new GameObject("PolicyAgreementView", typeof(RectTransform), typeof(Canvas));
        _created.Add(root);

        TMP_Text Text(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(root.transform, false);
            return go.GetComponent<TMP_Text>();
        }

        Button Button(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(root.transform, false);
            return go.GetComponent<Button>();
        }

        var view = root.AddComponent<PolicyAgreementView>();
        view.SetReferences(Text("Title"), Text("Message"), Button("Accept"), Text("AcceptLabel"),
                           Button("Terms"), Text("TermsLabel"), Button("Privacy"), Text("PrivacyLabel"));
        return view;
    }

    private static PolicyAgreementContent Content(string termsUrl = "https://t", string privacyUrl = "https://p") =>
        new("제목", "본문", "동의", "이용약관", termsUrl, "개인정보처리방침", privacyUrl);

    [Test]
    public void 문구를_바인딩한다()
    {
        var view = CreateViewTemplate();

        view.Bind(Content());

        Assert.AreEqual("제목", view.TitleText.text);
        Assert.AreEqual("동의", view.AcceptLabelText.text);
    }

    [Test]
    public void 링크가_비어_있으면_그_버튼을_숨긴다()
    {
        var view = CreateViewTemplate();

        view.Bind(Content(termsUrl: ""));

        Assert.IsFalse(view.TermsButton.gameObject.activeSelf);
        Assert.IsTrue(view.PrivacyButton.gameObject.activeSelf);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_버튼을_누르면_완료하고_창을_정리한다() => AwaitableTest.Run(async () =>
    {
        var template = CreateViewTemplate();
        var sut = new DefaultPolicyAgreementPrompt(template, new RecordingOpener());

        var showing = sut.ShowAsync(Content());
        var instance = sut.CurrentView;

        Assert.IsNotNull(instance, "창을 만들지 않았다");

        instance.AcceptButton.onClick.Invoke();
        await showing;

        Assert.IsTrue(instance == null, "수락 뒤 창이 남았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 링크_버튼은_URL을_연다() => AwaitableTest.Run(async () =>
    {
        var opener = new RecordingOpener();
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), opener);

        var showing = sut.ShowAsync(Content());
        sut.CurrentView.TermsButton.onClick.Invoke();
        sut.CurrentView.PrivacyButton.onClick.Invoke();
        sut.CurrentView.AcceptButton.onClick.Invoke();
        await showing;

        CollectionAssert.AreEqual(new[] { "https://t", "https://p" }, opener.Opened);
    });

    [Test]
    public void 프리팹이_없으면_수락하지_않고_예외를_던진다()
    {
        var sut = new DefaultPolicyAgreementPrompt(null, new RecordingOpener());

        Assert.Throws<System.InvalidOperationException>(() => sut.ShowAsync(Content()));
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator EventSystem이_없으면_표시하는_동안만_만든다() => AwaitableTest.Run(async () =>
    {
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), new RecordingOpener());

        var showing = sut.ShowAsync(Content());

        Assert.IsNotNull(Object.FindFirstObjectByType<EventSystem>(), "첫 씬보다 먼저 뜨면 버튼이 눌리지 않는다");

        sut.CurrentView.AcceptButton.onClick.Invoke();
        await showing;

        Assert.IsNull(Object.FindFirstObjectByType<EventSystem>(), "임시 EventSystem을 남겼다");
    });

    [Test]
    public void 이미_EventSystem이_있으면_만들지_않는다()
    {
        var existing = new GameObject("ES", typeof(EventSystem));
        _created.Add(existing);
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), new RecordingOpener());

        sut.ShowAsync(Content());

        Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
        sut.Close();
    }

    [Test]
    public void Close하면_창과_임시_EventSystem을_정리한다()
    {
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), new RecordingOpener());

        sut.ShowAsync(Content());
        var view = sut.CurrentView;
        sut.Close();

        Assert.IsTrue(view == null);
        Assert.IsNull(Object.FindFirstObjectByType<EventSystem>());
    }
}
