using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

public class UIStateButtonTest
{
    private GameObject _buttonGo;
    private GameObject _targetGo;
    private Image _target;
    private Sprite _normal;
    private Sprite _pressed;
    private Sprite _disabled;

    private static Sprite MakeSprite()
    {
        var tex = new Texture2D(1, 1);
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
    }

    [SetUp]
    public void SetUp()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();

        _buttonGo = new GameObject("button");
        _targetGo = new GameObject("target");
        _target = _targetGo.AddComponent<Image>();

        _normal = MakeSprite();
        _pressed = MakeSprite();
        _disabled = MakeSprite();
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_buttonGo != null) Object.DestroyImmediate(_buttonGo);
        if (_targetGo != null) Object.DestroyImmediate(_targetGo);
        if (_normal != null) Object.DestroyImmediate(_normal);
        if (_pressed != null) Object.DestroyImmediate(_pressed);
        if (_disabled != null) Object.DestroyImmediate(_disabled);
    }

    private UIStateButton NewButtonWithSet()
    {
        var set = new UIImageStateSet { Target = _target };
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _normal };
        set.Pressed = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _pressed };
        set.Disabled = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _disabled };

        var button = _buttonGo.AddComponent<UIStateButton>();
        button.SetSetsForTest(new List<UIImageStateSet> { set }, null);
        return button;
    }

    [Test]
    public void ApplyState에_각_상태를_넣으면_세트가_그_상태로_적용된다()
    {
        var button = NewButtonWithSet();

        button.ApplyState(UIButtonState.Pressed);
        Assert.AreSame(_pressed, _target.sprite);

        button.ApplyState(UIButtonState.Disabled);
        Assert.AreSame(_disabled, _target.sprite);

        button.ApplyState(UIButtonState.Selected);
        Assert.AreSame(_normal, _target.sprite, "Selected 미지정은 Normal로 떨어져야 한다");
    }

    [Test]
    public void interactable을_끄면_Disabled_세트가_적용된다()
    {
        var button = NewButtonWithSet();

        button.interactable = false;

        Assert.AreSame(_disabled, _target.sprite);
    }

    [Test]
    public void interactable을_다시_켜면_Normal_세트가_적용된다()
    {
        var button = NewButtonWithSet();
        button.interactable = false;

        button.interactable = true;

        Assert.AreSame(_normal, _target.sprite);
    }

    [Test]
    public void 세트가_비어도_상태_전이가_예외를_내지_않는다()
    {
        var button = _buttonGo.AddComponent<UIStateButton>();

        Assert.DoesNotThrow(() => button.interactable = false);
    }

    [Test]
    public void 텍스트_세트도_함께_적용된다()
    {
        var textGo = new GameObject("label");
        var text = textGo.AddComponent<Text>();

        var textSet = new UITextStateSet { Target = text };
        textSet.Normal = new UITextStateValue { Override = UITextSwap.Text, Text = "시작" };
        textSet.Disabled = new UITextStateValue { Override = UITextSwap.Text, Text = "잠김" };

        var button = _buttonGo.AddComponent<UIStateButton>();
        button.SetSetsForTest(null, new List<UITextStateSet> { textSet });

        button.ApplyState(UIButtonState.Disabled);

        Assert.AreEqual("잠김", text.text);
        Object.DestroyImmediate(textGo);
    }
}
