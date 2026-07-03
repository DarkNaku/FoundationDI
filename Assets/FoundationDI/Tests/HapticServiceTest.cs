using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class HapticServiceTest
{
    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("HAPTIC_ENABLED");
    }

    [Test]
    public void 활성화_상태에서_Selection_호출시_provider의_Selection을_호출한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Selection();

        provider.Received(1).Selection();
    }

    [Test]
    public void 활성화_상태에서_Impact_호출시_provider에_같은_스타일로_위임한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Impact(HapticImpact.Heavy);

        provider.Received(1).Impact(HapticImpact.Heavy);
    }

    [Test]
    public void 활성화_상태에서_Notification_호출시_provider에_같은_타입으로_위임한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Notification(HapticNotification.Warning);

        provider.Received(1).Notification(HapticNotification.Warning);
    }

    [Test]
    public void 비활성화_상태에서는_어떤_provider_메서드도_호출하지_않는다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = false };

        sut.Impact(HapticImpact.Medium);
        sut.Notification(HapticNotification.Success);
        sut.Selection();

        provider.DidNotReceive().Impact(Arg.Any<HapticImpact>());
        provider.DidNotReceive().Notification(Arg.Any<HapticNotification>());
        provider.DidNotReceive().Selection();
    }

    [Test]
    public void Enabled_기본값은_true이다()
    {
        var sut = new HapticService(Substitute.For<IHapticProvider>());

        Assert.IsTrue(sut.Enabled);
    }

    [Test]
    public void Enabled_설정값은_PlayerPrefs에_영속화된다()
    {
        new HapticService(Substitute.For<IHapticProvider>()).Enabled = false;

        var reloaded = new HapticService(Substitute.For<IHapticProvider>());

        Assert.IsFalse(reloaded.Enabled);
    }

    [Test]
    public void Noop_provider는_예외없이_모든_메서드를_수행한다()
    {
        var provider = new NoopHapticProvider();

        Assert.DoesNotThrow(() =>
        {
            provider.Impact(HapticImpact.Light);
            provider.Notification(HapticNotification.Error);
            provider.Selection();
        });
    }

    [Test]
    public void RegisterHapticService로_등록하면_IHapticService가_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterHapticService();
        var container = builder.Build();

        var haptic = container.Resolve<IHapticService>();

        Assert.IsNotNull(haptic);
        Assert.IsInstanceOf<HapticService>(haptic);
    }
}
