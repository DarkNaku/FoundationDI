using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Reflex.Core;
using Reflex.Enums;
using Resolution = Reflex.Enums.Resolution;

public class AdServiceRegistrationTest
{
    // 등록 그래프 전체를 검증하지는 않는다(그건 컨테이너 몫). 여기서는 컨테이너를 실제로
    // 빌드해서 IAdService가 해석되는지, Dispose가 예외 없이 끝나는지만 확인하는 스모크 테스트다.
    [Test]
    public void RegisterAdService로_등록하면_IAdService를_해석할_수_있고_컨테이너_Dispose가_예외를_던지지_않는다()
    {
        var settings = ScriptableObject.CreateInstance<AdServiceSettings>();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterAdService(settings);

            // 진짜 UnityAdDispatcher는 러너 GameObject를 만들며 DontDestroyOnLoad를 호출한다.
            // 플레이 모드 밖(EditMode 테스트)에서는 Unity가 이를 예외로 막는다. 여기서 검증하려는
            // 것은 그 MonoBehaviour 생명주기가 아니라 RegisterAdService의 등록 그래프 자체이므로,
            // 뒤에 다시 등록해서 덮어쓴다 — Reflex는 같은 계약이 여러 번 등록되면
            // Resolve<T>()는 마지막에 등록된 바인딩을 돌려준다.
            builder.RegisterType(typeof(FakeAdDispatcher),
                                 new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                                 Lifetime.Singleton, Resolution.Lazy);

            var container = builder.Build();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IConsentService가 등록되지 않았다"));

            var service = container.Resolve<IAdService>();

            Assert.IsNotNull(service);
            Assert.IsInstanceOf<AdService>(service);

            Assert.DoesNotThrow(() => container.Dispose());
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    private static Container BuildWithFakeDispatcher(AdServiceSettings settings, IAnalyticsService analytics)
    {
        var builder = new ContainerBuilder();
        builder.RegisterAdService(settings);
        builder.RegisterType(typeof(FakeAdDispatcher),
                             new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                             Lifetime.Singleton, Resolution.Lazy);
        if (analytics != null) builder.RegisterValue(analytics, new[] { typeof(IAnalyticsService) });
        return builder.Build();
    }

    [Test]
    public void 옵션이_켜져_있고_분석_서비스가_있으면_AdService를_해석할_때_ad_state를_설정한다()
    {
        var settings = ScriptableObject.CreateInstance<AdServiceSettings>();
        settings.ReportStateToAnalytics = true;
        var analytics = Substitute.For<IAnalyticsService>();

        try
        {
            using var container = BuildWithFakeDispatcher(settings, analytics);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IConsentService가 등록되지 않았다"));

            container.Resolve<IAdService>();

            analytics.Received(1).SetUserProperty("ad_state", "pending");
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 옵션이_꺼져_있으면_ad_state를_설정하지_않는다()
    {
        var settings = ScriptableObject.CreateInstance<AdServiceSettings>();
        var analytics = Substitute.For<IAnalyticsService>();

        try
        {
            using var container = BuildWithFakeDispatcher(settings, analytics);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IConsentService가 등록되지 않았다"));

            container.Resolve<IAdService>();

            analytics.DidNotReceive().SetUserProperty("ad_state", Arg.Any<string>());
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 옵션이_켜져_있어도_분석_서비스가_없으면_경고만_남긴다()
    {
        var settings = ScriptableObject.CreateInstance<AdServiceSettings>();
        settings.ReportStateToAnalytics = true;

        try
        {
            using var container = BuildWithFakeDispatcher(settings, analytics: null);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IConsentService가 등록되지 않았다"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IAnalyticsService가 등록되지 않았다"));

            Assert.IsNotNull(container.Resolve<IAdService>());
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }
}
