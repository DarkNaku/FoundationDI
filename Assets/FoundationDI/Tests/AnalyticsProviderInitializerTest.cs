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

    [Test]
    public void 분석_등록이_provider_초기화를_동의_선행_작업으로_노출한다()
    {
        var settings = ScriptableObject.CreateInstance<AnalyticsServiceSettings>();

        try
        {
            var builder = new Reflex.Core.ContainerBuilder();

            builder.RegisterAnalyticsService(settings);

            // 광고가 먼저 동의를 요청해도 ConsentService가 이 바인딩으로 분석 SDK 초기화를 먼저 기다린다.
            Assert.IsTrue(builder.HasBinding(typeof(IConsentPrerequisite)));
            Assert.IsTrue(builder.HasBinding(typeof(AnalyticsProviderInitializer)));
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }
}
