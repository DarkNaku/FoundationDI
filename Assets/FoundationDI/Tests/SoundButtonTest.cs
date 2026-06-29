using System.Reflection;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

public class SoundButtonTest
{
    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("sound-button");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    private static void SetPrivate(object obj, string field, object value)
    {
        obj.GetType()
           .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
           .SetValue(obj, value);
    }

    [Test]
    public void 클릭하면_주입된_사운드서비스로_지정키를_재생한다()
    {
        var sound = Substitute.For<ISoundService>();
        var button = _go.AddComponent<SoundButton>();   // EditMode에선 AddComponent가 Awake를 호출하지 않음 — Play()를 직접 호출해 검증.
        SetPrivate(button, "_sound", sound);
        SetPrivate(button, "_key", "Jump");

        button.Play();

        sound.Received(1).Play("Jump");
    }

    [Test]
    public void 사운드서비스가_없으면_에러를_남기고_재생하지_않는다()
    {
        var button = _go.AddComponent<SoundButton>();
        SetPrivate(button, "_key", "Jump");             // _sound는 null 유지

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("ISoundService"));

        Assert.DoesNotThrow(() => button.Play());
    }

    [Test]
    public void 버튼_onClick이_발생하면_지정키를_재생한다()
    {
        var sound = Substitute.For<ISoundService>();
        var button = _go.AddComponent<SoundButton>();
        SetPrivate(button, "_sound", sound);
        SetPrivate(button, "_key", "Jump");

        // EditMode에선 AddComponent가 Awake를 호출하지 않으므로 리플렉션으로 트리거 → onClick 리스너 등록
        typeof(SoundButton)
            .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(button, null);

        button.GetComponent<Button>().onClick.Invoke();   // 클릭 시뮬레이션

        sound.Received(1).Play("Jump");
    }

    [Test]
    public void RequireComponent로_Button이_자동_부착된다()
    {
        var button = _go.AddComponent<SoundButton>();

        Assert.IsNotNull(button.GetComponent<Button>());
    }
}
