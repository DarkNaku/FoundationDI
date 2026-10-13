using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AnalyticsProviderInitializerTest
{
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 성공한_provider만_활성으로_남긴다() => AwaitableTest.Run(async () =>
    {
        var ok = new FakeAnalyticsProvider("ok");
        var failed = new FakeAnalyticsProvider("failed") { InitializeResult = false };
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { ok, failed });

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("failed 초기화에 실패"));

        Assert.IsTrue(await sut.InitializeAsync());
        CollectionAssert.AreEqual(new[] { ok }, sut.Active);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동시에_불러도_SDK를_한_번만_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider { DeferInitialize = true };
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { provider });

        var first = sut.InitializeAsync();
        var second = sut.PrepareAsync();

        provider.CompleteInitialize(true);

        Assert.IsTrue(await first);
        await second;
        Assert.AreEqual(1, provider.InitializeCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 성공한_뒤에는_즉시_완료한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { provider });

        await sut.InitializeAsync();

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, provider.InitializeCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 전부_실패하면_다음_호출에서_재시도한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider { InitializeResult = false };
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { provider });

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("초기화에 실패"));
        Assert.IsFalse(await sut.InitializeAsync());

        provider.InitializeResult = true;

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(2, provider.InitializeCount);
    });
}
