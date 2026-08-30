using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class UIButtonTest
{
    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        // 정적 상태 초기화(이전 테스트 잔재 제거)
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("button");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    [Test]
    public void 서비스가_하나도_등록되지_않아도_클릭이_예외를_내지_않는다()
    {
        var button = _go.AddComponent<UIButton>();

        Assert.DoesNotThrow(() => button.onClick.Invoke());
    }

    [Test]
    public void 햅틱서비스가_등록되지_않아도_주입이_예외를_내지_않는다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(Substitute.For<ISoundService>()).As<ISoundService>();
        using var container = builder.Build();
        var button = _go.AddComponent<UIButton>();

        Assert.DoesNotThrow(() => button.Construct(container));
    }

    [Test]
    public void 사운드서비스가_등록되면_클릭시_지정한_SFX로_사운드를_만든다()
    {
        var sound = Substitute.For<ISoundService>();
        var sfx = SFX.FromTag("Click");
        var button = _go.AddComponent<UIButton>();
        button.ConfigureForTest(sfx, default, useHaptic: false, HapticImpact.Light);
        button.SetServicesForTest(sound, null);

        button.onClick.Invoke();

        sound.Received(1).CreateSound(sfx);
    }

    [Test]
    public void SFX를_지정하지_않으면_사운드를_만들지_않는다()
    {
        var sound = Substitute.For<ISoundService>();
        var button = _go.AddComponent<UIButton>();
        button.ConfigureForTest(SFX.Null, default, useHaptic: false, HapticImpact.Light);
        button.SetServicesForTest(sound, null);

        button.onClick.Invoke();

        sound.DidNotReceiveWithAnyArgs().CreateSound(default(SFX));
    }

    [Test]
    public void 햅틱을_켜면_클릭시_지정한_강도로_Impact를_부른다()
    {
        var haptic = Substitute.For<IHapticService>();
        var button = _go.AddComponent<UIButton>();
        button.ConfigureForTest(SFX.Null, default, useHaptic: true, HapticImpact.Heavy);
        button.SetServicesForTest(null, haptic);

        button.onClick.Invoke();

        haptic.Received(1).Impact(HapticImpact.Heavy, Arg.Any<float>());
    }

    [Test]
    public void 햅틱을_끄면_클릭해도_Impact를_부르지_않는다()
    {
        var haptic = Substitute.For<IHapticService>();
        var button = _go.AddComponent<UIButton>();
        button.ConfigureForTest(SFX.Null, default, useHaptic: false, HapticImpact.Heavy);
        button.SetServicesForTest(null, haptic);

        button.onClick.Invoke();

        haptic.DidNotReceiveWithAnyArgs().Impact(default, default);
    }

    // LogAssert는 경고 로그를 실패로 잡지 않으므로 "한 번만"을 검증하지 못한다.
    // 직접 세어야 한다.
    [Test]
    public void SFX가_지정됐는데_사운드서비스가_없으면_한_번만_경고한다()
    {
        var button = _go.AddComponent<UIButton>();
        button.ConfigureForTest(SFX.FromTag("Click"), default, useHaptic: false, HapticImpact.Light);
        button.SetServicesForTest(null, null);

        int warnings = 0;
        Application.LogCallback handler = (condition, stack, type) =>
        {
            if (type == LogType.Warning && condition.Contains("ISoundService")) warnings++;
        };

        Application.logMessageReceived += handler;
        try
        {
            button.onClick.Invoke();
            button.onClick.Invoke();
        }
        finally
        {
            Application.logMessageReceived -= handler;
        }

        Assert.AreEqual(1, warnings, "두 번 클릭해도 경고는 한 번이어야 한다");
    }
}
