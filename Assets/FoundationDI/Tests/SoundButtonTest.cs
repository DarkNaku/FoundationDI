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
        var button = _go.AddComponent<SoundButton>();   // RequireComponent로 Button 자동 부착, Awake 실행
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
    public void RequireComponent로_Button이_자동_부착된다()
    {
        var button = _go.AddComponent<SoundButton>();

        Assert.IsNotNull(button.GetComponent<Button>());
    }
}
