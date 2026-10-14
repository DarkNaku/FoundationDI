using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AnalyticsServiceConsentTest
{
    private static AnalyticsService Create(IAnalyticsProvider provider, IConsentService consent) =>
        new(new List<IAnalyticsProvider> { provider }, new AnalyticsServiceOptions(true), consent);

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의가_올_때까지_이벤트를_버퍼에_쥐고_있는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService { Defer = true };
        var sut = Create(provider, consent);

        sut.LogEvent("before_init");
        var initializing = sut.InitializeAsync();
        sut.LogEvent("waiting_consent");

        Assert.AreEqual(1, provider.InitializeCount, "동의보다 먼저 SDK를 초기화해야 한다(UMP 앞 Firebase)");
        Assert.AreEqual(0, provider.Events.Count, "동의 전에 이벤트를 보냈다");

        consent.Complete(ConsentResults.Granted());
        await initializing;

        CollectionAssert.AreEqual(new[] { "before_init", "waiting_consent" }, provider.Events.ConvertAll(e => e.Name));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 분석을_거부하면_수집을_끄고_버퍼를_버린다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = Create(provider, new FakeConsentService { Result = ConsentResults.Denied() });

        sut.LogEvent("before");
        await sut.InitializeAsync();
        sut.LogEvent("after");

        Assert.AreEqual(0, provider.Events.Count);
        Assert.AreEqual(false, provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
        Assert.IsTrue(sut.IsInitialized, "거부해도 게임은 진행된다 — 초기화 자체는 성공이다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 판정이_안_났으면_버퍼를_유지하고_재판정에서_flush한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        sut.LogEvent("offline_first_launch");
        await sut.InitializeAsync();
        sut.LogEvent("still_unknown");

        Assert.AreEqual(0, provider.Events.Count);

        consent.RaiseChanged(ConsentResults.Granted());

        CollectionAssert.AreEqual(new[] { "offline_first_launch", "still_unknown" },
                                  provider.Events.ConvertAll(e => e.Name));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 판정이_안_났다가_거부로_확정되면_버퍼를_버린다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        sut.LogEvent("buffered");
        await sut.InitializeAsync();
        consent.RaiseChanged(ConsentResults.Denied());
        consent.RaiseChanged(ConsentResults.Granted());

        Assert.AreEqual(0, provider.Events.Count, "거부로 확정된 시점의 버퍼가 나중에 새어 나갔다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 설정에서_동의를_철회하면_수집을_끈다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService();
        var sut = Create(provider, consent);

        await sut.InitializeAsync();
        consent.RaiseChanged(ConsentResults.Denied());
        sut.LogEvent("after_withdraw");

        Assert.AreEqual(0, provider.Events.Count);
        Assert.AreEqual(false, provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_번역을_flush_훅보다_먼저_부른다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeConsentAwareAnalyticsProvider();
        var sut = Create(provider, new FakeConsentService());

        await sut.InitializeAsync();

        var apply = provider.Calls.IndexOf("ApplyConsent:Granted");
        var hook = provider.Calls.IndexOf("OnBufferedStateFlushed");

        Assert.GreaterOrEqual(apply, 0);
        Assert.Less(apply, hook, "DMA 값이 첫 세션 지연 해제 뒤에 나가 인스톨 레코드에서 빠진다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 나중에_동의하면_그때_flush하고_훅을_한_번만_부른다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeConsentAwareAnalyticsProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Denied() };
        var sut = Create(provider, consent);

        await sut.InitializeAsync();

        Assert.AreEqual(0, provider.FlushHookCount);

        consent.RaiseChanged(ConsentResults.Granted());
        consent.RaiseChanged(ConsentResults.Denied());
        consent.RaiseChanged(ConsentResults.Granted());

        Assert.AreEqual(1, provider.FlushHookCount);
        Assert.AreEqual(4, provider.AppliedConsents.Count, "변경마다 번역을 다시 해야 한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 게임이_수집을_꺼_두면_동의해도_켜지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = Create(provider, new FakeConsentService());

        sut.CollectionEnabled = false;
        await sut.InitializeAsync();

        Assert.AreEqual(false, provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_동의_변경_구독을_푼다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService();
        var sut = Create(provider, consent);

        await sut.InitializeAsync();
        sut.Dispose();

        Assert.DoesNotThrow(() => consent.RaiseChanged(ConsentResults.Denied()));
    });
}
