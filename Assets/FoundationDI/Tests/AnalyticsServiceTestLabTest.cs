using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AnalyticsServiceTestLabTest
{
    private static AnalyticsService Create(IAnalyticsProvider provider, bool isTestLab, IConsentService consent = null) =>
        new(new List<IAnalyticsProvider> { provider }, new AnalyticsServiceOptions(true), consent,
            new FakeTestLabDetector { IsTestLab = isTestLab });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Test_Lab_기기면_이벤트를_보내지_않고_SDK_수집을_끈다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = Create(provider, true);

        sut.LogEvent("before");
        sut.SetUserProperty("tier", "gold");
        await sut.InitializeAsync();
        sut.LogEvent("after");

        Assert.IsTrue(sut.IsInitialized, "Test Lab에서도 게임은 진행된다 — 초기화 자체는 성공이다");
        Assert.AreEqual(0, provider.Events.Count);
        Assert.AreEqual(0, provider.Properties.Count);
        CollectionAssert.AreEqual(new[] { false }, provider.CollectionFlags);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Test_Lab_기기면_동의를_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        var consent = new FakeConsentService();
        var sut = Create(new FakeAnalyticsProvider(), true, consent);

        await sut.InitializeAsync();

        Assert.AreEqual(0, consent.RequestCount, "로봇이 동의 버튼을 눌러도 수집이 켜지면 안 된다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Test_Lab_기기면_flush_훅을_부르지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeFlushHookAnalyticsProvider();
        var sut = Create(provider, true);

        await sut.InitializeAsync();

        Assert.AreEqual(0, provider.FlushHookCount, "Adjust 첫 세션 지연이 풀리면 인스톨이 집계된다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Test_Lab_기기면_게임이_수집을_켜도_켜지지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = Create(provider, true);

        await sut.InitializeAsync();
        sut.CollectionEnabled = false;
        sut.CollectionEnabled = true;
        sut.LogEvent("after");

        Assert.AreEqual(0, provider.Events.Count);
        Assert.IsFalse(provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Test_Lab_기기가_아니면_평소대로_보낸다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeFlushHookAnalyticsProvider();
        var sut = Create(provider, false);

        sut.LogEvent("before");
        await sut.InitializeAsync();

        CollectionAssert.AreEqual(new[] { "before" }, provider.Events.ConvertAll(e => e.Name));
        Assert.AreEqual(1, provider.FlushHookCount);
    });
}
