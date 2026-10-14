using System.Collections;
using System.Linq;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using UnityEngine.TestTools;
using Resolution = Reflex.Enums.Resolution;

public class ConsentServiceRegistrationTest
{
    private ConsentServiceSettings _settings;

    [SetUp]
    public void SetUp() => _settings = ScriptableObject.CreateInstance<ConsentServiceSettings>();

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_settings);
        TrackingAuthorizationRegistry.Reset();
    }

    private Container Build(IPolicyAgreementPrompt prompt = null, params IConsentPrerequisite[] prerequisites)
    {
        var builder = new ContainerBuilder();
        builder.RegisterConsentService(_settings, prompt);

        // 진짜 디스패처는 러너 GameObject에 DontDestroyOnLoad를 부른다(EditMode에서 예외).
        builder.RegisterType(typeof(FakeAdDispatcher), new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                             Lifetime.Singleton, Resolution.Lazy);

        // 기본 저장소는 PlayerPrefs를 건드리므로 테스트는 메모리 대역으로 덮는다.
        builder.RegisterValue(new FakePolicyStorage { Version = 0 }, new[] { typeof(IPolicyAgreementStorage) });

        foreach (var prerequisite in prerequisites)
        {
            builder.RegisterValue(prerequisite, new[] { typeof(IConsentPrerequisite) });
        }

        return builder.Build();
    }

    [Test]
    public void 등록하면_IConsentService를_해석한다()
    {
        var container = Build(new FakePolicyPrompt(new System.Collections.Generic.List<string>()));

        Assert.IsInstanceOf<ConsentService>(container.Resolve<IConsentService>());
        Assert.DoesNotThrow(() => container.Dispose());
    }

    [Test]
    public void 설정이_null이면_등록하지_않는다()
    {
        var builder = new ContainerBuilder();

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ConsentServiceSettings"));

        builder.RegisterConsentService(null);

        Assert.IsFalse(builder.HasBinding(typeof(IConsentService)));
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 넘긴_약관_창을_쓰고_등록된_선행_작업을_기다린다() => AwaitableTest.Run(async () =>
    {
        var log = new System.Collections.Generic.List<string>();
        var prompt = new FakePolicyPrompt(log);
        var prerequisite = new FakeConsentPrerequisite(log);
        var consent = Build(prompt, prerequisite).Resolve<IConsentService>();

        var requesting = consent.RequestAsync();
        prompt.Accept();
        await requesting;

        CollectionAssert.AreEqual(new[] { "policy", "prereq" }, log);
    });

    [Test]
    public void 릴리스_빌드에서는_디버그_지역을_끈다()
    {
        Assert.AreEqual(UmpDebugGeography.Disabled, _settings.ToOptions(isDebugBuild: false).UmpRequest.DebugGeography);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 추적만_묻는_동의는_약관과_UMP_없이_ATT만_묻는다() => AwaitableTest.Run(async () =>
    {
        var tracking = new FakeTrackingAuthorization();
        var sut = ConsentService.CreateTrackingOnly(new FakeAdDispatcher(), tracking);

        var requesting = sut.RequestAsync();

        Assert.IsTrue(tracking.IsWaiting);

        tracking.Respond(TrackingAuthorizationStatus.Authorized);
        var result = await requesting;

        Assert.IsTrue(result.PolicyAccepted);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, result.TrackingStatus);
    });
}
