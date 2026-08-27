using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using VContainer;

// 어댑터 고유 설정을 코어가 "내용을 모른 채" 어댑터까지 실어 나르는 경로를 검증한다.
// Adjust처럼 이름→토큰 매핑표가 필요한 SDK가 이 경로를 탄다(AnalyticsService README 2.3).
public class AnalyticsProviderSettingsTest
{
    private readonly List<Object> _created = new();

    [SetUp]
    public void SetUp() => AnalyticsProviderRegistry.Reset();

    [TearDown]
    public void TearDown()
    {
        AnalyticsProviderRegistry.Reset();

        foreach (var asset in _created) Object.DestroyImmediate(asset);

        _created.Clear();
    }

    private T Create<T>() where T : ScriptableObject
    {
        var asset = ScriptableObject.CreateInstance<T>();
        _created.Add(asset);
        return asset;
    }

    [Test]
    public void provider_설정_목록에서_요청한_타입을_찾아_준다()
    {
        var mine = Create<FakeAnalyticsProviderSettings>();
        mine.Token = "abc123";

        var context = new AnalyticsProviderCreationContext(
            new AnalyticsServiceOptions(true),
            new AnalyticsProviderSettings[] { Create<OtherAnalyticsProviderSettings>(), mine });

        Assert.AreSame(mine, context.GetSettings<FakeAnalyticsProviderSettings>());
    }

    [Test]
    public void provider_설정_목록에_없는_타입을_요청하면_null을_준다()
    {
        // 예외가 아니라 null인 이유: 설정이 없을 때의 대응이 어댑터마다 다르다.
        // Adjust는 앱 토큰이 없으면 초기화를 실패시켜야 하지만, 전부 선택값인 어댑터도 있다.
        var context = new AnalyticsProviderCreationContext(
            new AnalyticsServiceOptions(true),
            new AnalyticsProviderSettings[] { Create<OtherAnalyticsProviderSettings>() });

        Assert.IsNull(context.GetSettings<FakeAnalyticsProviderSettings>());
    }

    [Test]
    public void 기본_생성한_컨텍스트에_설정을_요청해도_예외가_나지_않는다()
    {
        // default(struct)는 생성자를 타지 않아 ProviderSettings가 진짜 null이다.
        var context = default(AnalyticsProviderCreationContext);

        Assert.IsNull(context.GetSettings<FakeAnalyticsProviderSettings>());
    }

    [Test]
    public void 팩토리가_provider_설정을_creator에게_그대로_넘긴다()
    {
        var mine = Create<FakeAnalyticsProviderSettings>();
        mine.Token = "xyz789";

        FakeAnalyticsProviderSettings seen = null;

        AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug, ctx =>
        {
            seen = ctx.GetSettings<FakeAnalyticsProviderSettings>();
            return new FakeAnalyticsProvider("Debug");
        });

        new AnalyticsProviderFactory().CreateAll(
            AnalyticsProviderType.Debug,
            new AnalyticsServiceOptions(true),
            new AnalyticsProviderSettings[] { mine });

        Assert.AreSame(mine, seen);
        Assert.AreEqual("xyz789", seen.Token);
    }

    [Test]
    public void 설정에_담긴_provider_설정_목록이_등록_경로를_타고_creator까지_간다()
    {
        var mine = Create<FakeAnalyticsProviderSettings>();
        var settings = Create<AnalyticsServiceSettings>();

        // _providerSettings는 [SerializeField] private이라 인스펙터가 채우는 자리다.
        var serialized = new UnityEditor.SerializedObject(settings);
        var list = serialized.FindProperty("_providerSettings");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = mine;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        FakeAnalyticsProviderSettings seen = null;

        AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug, ctx =>
        {
            seen = ctx.GetSettings<FakeAnalyticsProviderSettings>();
            return new FakeAnalyticsProvider("Debug");
        });

        var builder = new ContainerBuilder();
        builder.RegisterAnalyticsService(settings);

        using (var container = builder.Build())
        {
            container.Resolve<IAnalyticsService>();
        }

        Assert.AreSame(mine, seen, "설정 목록이 등록 경로에서 끊겼다");
    }
}
