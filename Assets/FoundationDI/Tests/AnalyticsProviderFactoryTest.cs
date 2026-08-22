using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AnalyticsProviderFactoryTest
{
    // AnalyticsProviderRegistry는 프로세스 전역 정적 상태다. 어떤 테스트가 등록해 두고 정리하지
    // 않으면 다른 테스트가 그 잔재를 물려받는다. 매 테스트 앞뒤로 리셋한다.
    [SetUp]
    public void SetUp() => AnalyticsProviderRegistry.Reset();

    [TearDown]
    public void TearDown() => AnalyticsProviderRegistry.Reset();

    [Test]
    public void creator가_없는_provider만_건너뛰고_나머지를_생성한다()
    {
        AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug,
                                           _ => new FakeAnalyticsProvider("Debug"));

        var factory = new AnalyticsProviderFactory();

        // AdService와 달리 폴백하지 않는다. provider가 여럿인 이상 나머지가 계속 도는 것이 옳다.
        LogAssert.Expect(LogType.Error, new Regex("Firebase"));

        var providers = factory.CreateAll(
            AnalyticsProviderType.Debug | AnalyticsProviderType.Firebase,
            new AnalyticsServiceOptions(true));

        Assert.AreEqual(1, providers.Count, "등록된 provider까지 함께 버려졌다");
        Assert.AreEqual("Debug", providers[0].Name);
    }

    [Test]
    public void 같은_타입을_다시_등록하면_예외_없이_교체된다()
    {
        // 도메인 리로드와 에디터 재실행이 [RuntimeInitializeOnLoadMethod] 경로를 여러 번 태운다.
        AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug,
                                           _ => new FakeAnalyticsProvider("First"));
        AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug,
                                           _ => new FakeAnalyticsProvider("Second"));

        var providers = new AnalyticsProviderFactory()
            .CreateAll(AnalyticsProviderType.Debug, new AnalyticsServiceOptions(true));

        Assert.AreEqual(1, providers.Count);
        Assert.AreEqual("Second", providers[0].Name);
    }

    [Test]
    public void None을_요청하면_아무것도_생성하지_않고_에러도_내지_않는다()
    {
        var providers = new AnalyticsProviderFactory()
            .CreateAll(AnalyticsProviderType.None, new AnalyticsServiceOptions(true));

        Assert.AreEqual(0, providers.Count);
    }
}
