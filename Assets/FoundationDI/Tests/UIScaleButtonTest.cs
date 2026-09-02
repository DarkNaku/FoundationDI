using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer;

public class UIScaleButtonTest
{
    private const float Tolerance = 1e-4f;
    private const float Highlighted = 1.2f;
    private const float Pressed = 0.9f;

    private GameObject _buttonGo;
    private RectTransform _content;

    [SetUp]
    public void SetUp()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();

        _buttonGo = new GameObject("button", typeof(RectTransform));

        var contentGo = new GameObject("content", typeof(RectTransform));
        _content = contentGo.GetComponent<RectTransform>();
        _content.SetParent(_buttonGo.transform, false);
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_buttonGo != null) Object.DestroyImmediate(_buttonGo);
    }

    private UIScaleButton NewButton(float duration = 0f)
    {
        var button = _buttonGo.AddComponent<UIScaleButton>();
        button.ConfigureScaleForTest(_content, Highlighted, Pressed, duration);
        return button;
    }

    // EventSystem 없이 Selectable의 포인터 경로를 그대로 탄다.
    private static PointerEventData NewPointer() => new PointerEventData(EventSystem.current);

    [Test]
    public void 포인터가_들어오고_눌리고_떼고_나가는_동안_목표_배율이_따라간다()
    {
        var button = NewButton();

        button.OnPointerEnter(NewPointer());
        Assert.AreEqual(Highlighted, button.TargetScale, Tolerance, "호버하면 커져야 한다");

        button.OnPointerDown(NewPointer());
        Assert.AreEqual(Pressed, button.TargetScale, Tolerance, "누르면 작아져야 한다");

        button.OnPointerUp(NewPointer());
        Assert.AreEqual(Highlighted, button.TargetScale, Tolerance, "떼면 다시 커져야 한다");

        button.OnPointerExit(NewPointer());
        Assert.AreEqual(1f, button.TargetScale, Tolerance, "밖으로 나가면 본래 크기로 돌아가야 한다");
    }

    [Test]
    public void 누른_채_밖으로_끌면_본래_배율로_돌아간다()
    {
        var button = NewButton();
        button.OnPointerEnter(NewPointer());
        button.OnPointerDown(NewPointer());

        button.OnPointerExit(NewPointer());

        Assert.AreEqual(1f, button.TargetScale, Tolerance);
    }

    [Test]
    public void 비활성이면_호버_중이어도_본래_배율로_떨어진다()
    {
        var button = NewButton();
        button.OnPointerEnter(NewPointer());

        button.interactable = false;
        button.RefreshTarget();

        Assert.AreEqual(1f, button.TargetScale, Tolerance, "Disabled 오버라이드가 없으면 Normal로 떨어져야 한다");
    }

    [Test]
    public void 비활성_배율을_오버라이드하면_그_배율을_쓴다()
    {
        var button = NewButton();
        button.ConfigureDisabledScaleForTest(true, 0.8f);

        button.interactable = false;
        button.RefreshTarget();

        Assert.AreEqual(0.8f, button.TargetScale, Tolerance);
    }

    [Test]
    public void 다시_활성화하면_유지된_호버_상태가_그대로_반영된다()
    {
        var button = NewButton();
        button.OnPointerEnter(NewPointer());
        button.interactable = false;
        button.RefreshTarget();

        button.interactable = true;
        button.RefreshTarget();

        Assert.AreEqual(Highlighted, button.TargetScale, Tolerance);
    }

    [Test]
    public void 지정한_시간_동안_커브로_보간해_목표_배율에_도달한다()
    {
        var button = NewButton(0.2f);
        button.ConfigureCurveForTest(AnimationCurve.Linear(0f, 0f, 1f, 1f));

        button.OnPointerEnter(NewPointer());
        Assert.AreEqual(1f, button.CurrentScale, Tolerance, "목표가 바뀐 직후에는 아직 본래 배율이다");

        button.Tick(0.1f);
        Assert.AreEqual(1f + (Highlighted - 1f) * 0.5f, button.CurrentScale, Tolerance, "절반 시점");

        button.Tick(0.1f);
        Assert.AreEqual(Highlighted, button.CurrentScale, Tolerance, "끝나면 정확히 목표 배율이다");

        button.Tick(1f);
        Assert.AreEqual(Highlighted, button.CurrentScale, Tolerance, "끝난 뒤에는 더 움직이지 않는다");
    }

    [Test]
    public void 지정_시간이_0이면_목표_배율이_즉시_적용된다()
    {
        var button = NewButton();

        button.OnPointerEnter(NewPointer());

        Assert.AreEqual(Highlighted, button.CurrentScale, Tolerance);
    }

    [Test]
    public void 보간_도중_목표가_바뀌면_현재_배율에서_이어서_보간한다()
    {
        var button = NewButton(0.2f);
        button.ConfigureCurveForTest(AnimationCurve.Linear(0f, 0f, 1f, 1f));

        button.OnPointerEnter(NewPointer());
        button.Tick(0.1f);

        var half = 1f + (Highlighted - 1f) * 0.5f;
        Assert.AreEqual(half, button.CurrentScale, Tolerance);

        button.OnPointerDown(NewPointer());
        Assert.AreEqual(half, button.CurrentScale, Tolerance, "목표가 바뀌는 순간 현재 배율이 튀면 안 된다");

        button.Tick(0.1f);
        Assert.AreEqual(half + (Pressed - half) * 0.5f, button.CurrentScale, Tolerance, "새 출발점에서 다시 절반");

        button.Tick(0.1f);
        Assert.AreEqual(Pressed, button.CurrentScale, Tolerance);
    }

    [Test]
    public void 스케일은_지정한_자식에만_걸리고_버튼_자신은_변하지_않는다()
    {
        var button = NewButton();

        button.OnPointerEnter(NewPointer());

        Assert.AreEqual(Highlighted, _content.localScale.x, Tolerance);
        Assert.AreEqual(Highlighted, _content.localScale.y, Tolerance);
        Assert.AreEqual(1f, _buttonGo.transform.localScale.x, Tolerance,
            "버튼 자신이 커지면 히트 영역이 함께 변해 진동이 생긴다");
    }

    [Test]
    public void 자식의_원래_스케일을_기준으로_배율이_곱해진다()
    {
        _content.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        var button = NewButton();

        button.OnPointerEnter(NewPointer());
        Assert.AreEqual(0.5f * Highlighted, _content.localScale.x, Tolerance);

        button.OnPointerExit(NewPointer());
        Assert.AreEqual(0.5f, _content.localScale.x, Tolerance, "본래 크기로 정확히 돌아와야 한다");
    }

    [Test]
    public void 스케일_타깃이_없어도_예외_없이_동작한다()
    {
        var button = _buttonGo.AddComponent<UIScaleButton>();
        button.ConfigureScaleForTest(null, Highlighted, Pressed, 0.2f);

        Assert.DoesNotThrow(() =>
        {
            button.OnPointerEnter(NewPointer());
            button.Tick(0.1f);
            button.OnPointerExit(NewPointer());
        });

        Assert.AreEqual(1f, button.TargetScale, Tolerance);
    }
}
