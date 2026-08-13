using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class HapticServiceTest
{
    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("HAPTIC_ENABLED");
    }

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

    [Test]
    public void 비활성화_호출은_공유_쿨다운_타임스탬프를_소비하지_않는다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t);

        sut.Enabled = false;
        sut.Impact(HapticImpact.Medium, cooldown: 0.02f); // 비활성 → 무시, 타임스탬프 미소비
        sut.Enabled = true;
        t = 100.005f;                                       // +5ms (쿨다운 창 안)
        sut.Impact(HapticImpact.Medium, cooldown: 0.02f);   // 직전 소비가 없었으니 발동해야 함

        provider.Received(1).Impact(HapticImpact.Medium);
    }

    [UnityTest]
    public IEnumerator 활성화시_Play는_provider_PlayAsync에_위임한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var source = new AwaitableCompletionSource();
        provider.PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>()).Returns(source.Awaitable);
        var sut = new HapticService(provider) { Enabled = true };

        var p = sut.Play(default(HapticCurve));
        _ = provider.Received(1).PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>());

        source.SetResult();
        await p;
    });

    [UnityTest]
    public IEnumerator 비활성화시_Play는_provider를_호출하지_않고_즉시완료된다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = false };

        await sut.Play(default(HapticCurve));

        _ = provider.DidNotReceive().PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>());
    });

    [UnityTest]
    public IEnumerator 새_Play는_이전_재생을_취소하고_Stop을_호출한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var tokens = new List<CancellationToken>();
        var s1 = new AwaitableCompletionSource();
        var s2 = new AwaitableCompletionSource();
        provider.PlayAsync(Arg.Any<HapticCurve>(), Arg.Do<CancellationToken>(tokens.Add))
                .Returns(s1.Awaitable, s2.Awaitable);
        var sut = new HapticService(provider) { Enabled = true };

        var p1 = sut.Play(default(HapticCurve));   // in-flight
        var p2 = sut.Play(default(HapticCurve));   // 이전 취소

        Assert.IsTrue(tokens[0].IsCancellationRequested, "첫 재생의 토큰이 취소되어야 한다");
        Assert.IsFalse(tokens[1].IsCancellationRequested, "두번째 재생은 진행 중이어야 한다");
        provider.Received(1).Stop();

        s1.SetResult(); s2.SetResult();
        await p2;
    });

    [UnityTest]
    public IEnumerator Play_중에는_IsPlaying이_true고_완료후_false다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var source = new AwaitableCompletionSource();
        provider.PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>()).Returns(source.Awaitable);
        var sut = new HapticService(provider) { Enabled = true };

        var p = sut.Play(default(HapticCurve));
        Assert.IsTrue(sut.IsPlaying);

        source.SetResult();
        await p;
        Assert.IsFalse(sut.IsPlaying);
    });
}
