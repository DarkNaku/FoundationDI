using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class UIImageStateSetTest
{
    private GameObject _go;
    private Image _target;
    private Sprite _a;
    private Sprite _b;

    private static Sprite MakeSprite()
    {
        var tex = new Texture2D(1, 1);
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
    }

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("target");
        _target = _go.AddComponent<Image>();
        _a = MakeSprite();
        _b = MakeSprite();
    }

    [TearDown]
    public void TearDown()
    {
        if (_go != null) Object.DestroyImmediate(_go);
        if (_a != null) Object.DestroyImmediate(_a);
        if (_b != null) Object.DestroyImmediate(_b);
    }

    private UIImageStateSet NewSet() => new UIImageStateSet { Target = _target };

    [Test]
    public void 상태가_필드를_오버라이드하면_그_상태의_값을_쓴다()
    {
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _a };
        set.Pressed = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _b };

        set.Apply(UIButtonState.Pressed);

        Assert.AreSame(_b, _target.sprite);
    }

    [Test]
    public void 상태가_오버라이드하지_않으면_Normal_값으로_떨어진다()
    {
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _a };
        set.Pressed = new UIImageStateValue { Override = UIImageSwap.None };

        set.Apply(UIButtonState.Pressed);

        Assert.AreSame(_a, _target.sprite);
    }

    [Test]
    public void Normal도_오버라이드하지_않으면_그_필드를_건드리지_않는다()
    {
        _target.sprite = _a;
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.red };
        set.Pressed = new UIImageStateValue { Override = UIImageSwap.None };

        set.Apply(UIButtonState.Pressed);

        Assert.AreSame(_a, _target.sprite, "아무도 Sprite를 오버라이드하지 않았으므로 원본이 유지돼야 한다");
    }

    [Test]
    public void Selected를_지정하지_않으면_Normal로_떨어진다()
    {
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _a };
        set.Highlighted = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _b };
        set.Selected = new UIImageStateValue { Override = UIImageSwap.None };

        set.Apply(UIButtonState.Selected);

        Assert.AreSame(_a, _target.sprite, "Highlighted가 아니라 Normal로 떨어져야 모바일 stuck 하이라이트를 막는다");
    }

    [Test]
    public void 색만_오버라이드하면_스프라이트는_원본_그대로다()
    {
        _target.sprite = _a;
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.white };
        set.Disabled = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.gray };

        set.Apply(UIButtonState.Disabled);

        Assert.AreEqual(Color.gray, _target.color);
        Assert.AreSame(_a, _target.sprite);
    }

    [Test]
    public void 타깃이_null이면_예외_없이_아무_일도_하지_않는다()
    {
        var set = new UIImageStateSet { Target = null };
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Sprite, Sprite = _a };

        Assert.DoesNotThrow(() => set.Apply(UIButtonState.Normal));
    }

    [Test]
    public void Visible_오버라이드는_타깃의_enabled를_바꾼다()
    {
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Visible, Visible = true };
        set.Disabled = new UIImageStateValue { Override = UIImageSwap.Visible, Visible = false };

        set.Apply(UIButtonState.Disabled);

        Assert.IsFalse(_target.enabled);
        Assert.IsTrue(_go.activeSelf, "SetActive가 아니라 enabled만 꺼야 레이아웃이 흔들리지 않는다");
    }

    [Test]
    public void Normal이_오버라이드하지_않는_필드도_상태를_벗어나면_원래_값으로_돌아온다()
    {
        _target.color = Color.white;                       // 프리팹 값
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.None };
        set.Highlighted = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.yellow };

        set.Apply(UIButtonState.Normal);                   // 첫 Apply에서 기준값 캡처
        set.Apply(UIButtonState.Highlighted);
        Assert.AreEqual(Color.yellow, _target.color);

        set.Apply(UIButtonState.Normal);

        Assert.AreEqual(Color.white, _target.color, "Normal이 오버라이드하지 않아도 기준값으로 돌아와야 한다");
    }

    [Test]
    public void 아무_상태도_오버라이드하지_않는_필드는_계속_건드리지_않는다()
    {
        _target.sprite = _a;
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.white };
        set.Pressed = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.gray };

        set.Apply(UIButtonState.Pressed);
        _target.sprite = _b;                               // 외부 코드가 바꾼 상황
        set.Apply(UIButtonState.Normal);

        Assert.AreSame(_b, _target.sprite, "아무도 Sprite를 관리하지 않으므로 기준값으로도 되돌리면 안 된다");
    }

    [Test]
    public void 기준값은_한_번만_캡처되어_재사용시에도_첫_값을_유지한다()
    {
        _target.color = Color.white;
        var set = NewSet();
        set.Normal = new UIImageStateValue { Override = UIImageSwap.None };
        set.Highlighted = new UIImageStateValue { Override = UIImageSwap.Color, Color = Color.yellow };

        set.Apply(UIButtonState.Highlighted);              // 첫 Apply가 곧 캡처 시점
        set.Apply(UIButtonState.Highlighted);              // 재캡처되면 기준값이 yellow로 오염된다
        set.Apply(UIButtonState.Normal);

        Assert.AreEqual(Color.white, _target.color);
    }
}
