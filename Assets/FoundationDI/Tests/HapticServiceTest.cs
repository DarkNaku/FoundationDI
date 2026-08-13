using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class HapticServiceTest
{
    [Test]
    public void Noop_provider의_프리셋은_예외없이_무동작한다()
    {
        var provider = new NoopHapticProvider();

        Assert.DoesNotThrow(() =>
        {
            provider.Impact(HapticImpact.Light);
            provider.Notification(HapticNotification.Error);
            provider.Selection();
            provider.Stop();
            provider.Prewarm();
        });
    }

    [UnityTest]
    public IEnumerator Noop_provider의_PlayAsync는_즉시완료된다() => UniTask.ToCoroutine(async () =>
    {
        var provider = new NoopHapticProvider();

        var a = provider.PlayAsync(default(HapticCurve), CancellationToken.None);
        Assert.IsTrue(a.IsCompleted);
        await a;
    });

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
    public void 활성화_상태에서_Impact는_provider에_같은_스타일로_위임한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };
        sut.Impact(HapticImpact.Heavy, cooldown: 0f);
        provider.Received(1).Impact(HapticImpact.Heavy);
    }

    [Test]
    public void 비활성화_상태에서는_어떤_provider_프리셋도_호출하지_않는다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = false };
        sut.Impact(HapticImpact.Medium, cooldown: 0f);
        sut.Notification(HapticNotification.Success, cooldown: 0f);
        sut.Selection(cooldown: 0f);
        provider.DidNotReceive().Impact(Arg.Any<HapticImpact>());
        provider.DidNotReceive().Notification(Arg.Any<HapticNotification>());
        provider.DidNotReceive().Selection();
    }

    [Test]
    public void 쿨다운_간격_내_재호출은_무시된다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t) { Enabled = true };
        sut.Impact(HapticImpact.Medium, cooldown: 0.02f); // t=100 발동
        t = 100.01f;                                       // +10ms < 20ms
        sut.Impact(HapticImpact.Medium, cooldown: 0.02f); // 무시
        provider.Received(1).Impact(HapticImpact.Medium);
    }

    [Test]
    public void cooldown_0이면_항상_발동한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t) { Enabled = true };
        sut.Impact(HapticImpact.Medium, cooldown: 0f);
        sut.Impact(HapticImpact.Medium, cooldown: 0f);
        provider.Received(2).Impact(HapticImpact.Medium);
    }

    [Test]
    public void 쿨다운은_프리셋_전체가_단일_타임스탬프를_공유한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t) { Enabled = true };
        sut.Impact(HapticImpact.Light, cooldown: 0.02f); // 발동
        t = 100.005f;                                     // +5ms
        sut.Selection(cooldown: 0.02f);                   // 공유 타임스탬프 → 무시
        provider.Received(1).Impact(HapticImpact.Light);
        provider.DidNotReceive().Selection();
    }
}
