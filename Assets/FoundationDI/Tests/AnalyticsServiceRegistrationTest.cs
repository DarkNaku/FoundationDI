using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class AnalyticsServiceRegistrationTest
{
    [SetUp]
    public void SetUp()
    {
        AnalyticsProviderRegistry.Reset();
        AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug,
                                           _ => new FakeAnalyticsProvider("Debug"));
    }

    [TearDown]
    public void TearDown() => AnalyticsProviderRegistry.Reset();

    // 등록 그래프 전체를 검증하지는 않는다(그건 VContainer 몫). 컨테이너를 실제로 빌드해서
    // IAnalyticsService가 싱글턴으로 해석되는지, Dispose가 예외 없이 끝나는지만 본다.
    [Test]
    public void RegisterAnalyticsService로_등록하면_IAnalyticsService가_싱글턴으로_해석된다()
    {
        var settings = ScriptableObject.CreateInstance<AnalyticsServiceSettings>();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterAnalyticsService(settings);

            var container = builder.Build();

            var first = container.Resolve<IAnalyticsService>();
            var second = container.Resolve<IAnalyticsService>();

            Assert.IsNotNull(first);
            Assert.IsInstanceOf<AnalyticsService>(first);
            Assert.AreSame(first, second, "싱글턴으로 등록되지 않았다");

            Assert.DoesNotThrow(() => container.Dispose());
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void settings가_null이면_에러_로그만_남기고_등록하지_않는다()
    {
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AnalyticsServiceSettings"));

        var builder = new ContainerBuilder();
        builder.RegisterAnalyticsService(null);

        var container = builder.Build();

        Assert.Throws<VContainerException>(() => container.Resolve<IAnalyticsService>());

        container.Dispose();
    }
}
