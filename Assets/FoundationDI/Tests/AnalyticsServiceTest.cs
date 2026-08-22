using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AnalyticsServiceTest
{
    private static AnalyticsServiceOptions NewOptions(bool collectionEnabled = true) =>
        new(collectionEnabled);

    [Test]
    public void 컬렉션_초기화가_파라미터의_순서와_타입을_보존한다()
    {
        var parameters = new AnalyticsParams
        {
            { "level", 12L },
            { "clear_time", 34.5 },
            { "difficulty", "hard" },
        };

        var items = parameters.ToList();

        Assert.AreEqual(3, parameters.Count);

        Assert.AreEqual("level", items[0].Key);
        Assert.AreEqual(AnalyticsParamKind.Long, items[0].Value.Kind);
        Assert.AreEqual(12L, items[0].Value.LongValue);

        Assert.AreEqual("clear_time", items[1].Key);
        Assert.AreEqual(AnalyticsParamKind.Double, items[1].Value.Kind);
        Assert.AreEqual(34.5, items[1].Value.DoubleValue, 0.0001);

        Assert.AreEqual("difficulty", items[2].Key);
        Assert.AreEqual(AnalyticsParamKind.String, items[2].Value.Kind);
        Assert.AreEqual("hard", items[2].Value.StringValue);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이벤트를_발행하면_모든_provider가_각각_한_번씩_받는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var a = new FakeAnalyticsProvider("A");
        var b = new FakeAnalyticsProvider("B");
        var sut = new AnalyticsService(new IAnalyticsProvider[] { a, b }, NewOptions());

        await sut.InitializeAsync();
        sut.LogEvent("boss_defeated");

        Assert.AreEqual(1, a.Events.Count, "A가 이벤트를 받지 못했다");
        Assert.AreEqual("boss_defeated", a.Events[0].Name);
        Assert.AreEqual(1, b.Events.Count, "B가 이벤트를 받지 못했다");
        Assert.AreEqual("boss_defeated", b.Events[0].Name);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 한_provider가_예외를_던져도_나머지_provider는_호출된다() =>
        UniTask.ToCoroutine(async () =>
    {
        var broken = new FakeAnalyticsProvider("Broken") { ThrowOnLogEvent = true };
        var healthy = new FakeAnalyticsProvider("Healthy");
        var sut = new AnalyticsService(new IAnalyticsProvider[] { broken, healthy }, NewOptions());

        await sut.InitializeAsync();

        LogAssert.Expect(LogType.Error, new Regex("Broken"));
        sut.LogEvent("boss_defeated");

        Assert.AreEqual(1, healthy.Events.Count, "예외 뒤의 정상 provider가 호출되지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전_이벤트는_버퍼링됐다가_초기화_후_순서대로_전달된다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = new AnalyticsService(new IAnalyticsProvider[] { provider }, NewOptions());

        sut.LogEvent("first");
        sut.LogPurchase(new PurchaseInfo("gem_pack", 4.99, "USD"));
        sut.LogEvent("second");

        Assert.AreEqual(0, provider.Events.Count, "초기화 전인데 provider로 새어 나갔다");

        await sut.InitializeAsync();

        CollectionAssert.AreEqual(
            new[] { "LogEvent:first", "LogPurchase:gem_pack", "LogEvent:second" },
            provider.Calls.Where(c => !c.StartsWith("Initialize")).ToList());
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_전_SetUserProperty는_같은_키의_마지막_값만_전달된다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = new AnalyticsService(new IAnalyticsProvider[] { provider }, NewOptions());

        sut.SetUserProperty("player_level", "12");
        sut.SetUserProperty("player_level", "24");
        sut.SetUserProperty("player_level", "37");
        sut.SetUserId("player-a");
        sut.SetUserId("player-b");

        await sut.InitializeAsync();

        CollectionAssert.AreEqual(new[] { ("player_level", "37") }, provider.Properties);
        CollectionAssert.AreEqual(new[] { "player-b" }, provider.UserIds);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화_시_유저_상태가_버퍼된_이벤트보다_먼저_전달된다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = new AnalyticsService(new IAnalyticsProvider[] { provider }, NewOptions());

        // 이벤트를 먼저 발행해도, flush는 유저 귀속을 먼저 붙인 뒤 이벤트를 내보내야 한다.
        sut.LogEvent("tutorial_start");
        sut.SetUserId("player-a");
        sut.SetUserProperty("cohort", "2026-08");

        await sut.InitializeAsync();

        var calls = provider.Calls.Where(c => !c.StartsWith("Initialize")).ToList();
        var userIdIndex = calls.IndexOf("SetUserId:player-a");
        var propertyIndex = calls.IndexOf("SetUserProperty:cohort=2026-08");
        var eventIndex = calls.IndexOf("LogEvent:tutorial_start");

        Assert.Greater(userIdIndex, -1, "SetUserId가 전달되지 않았다");
        Assert.Greater(propertyIndex, -1, "SetUserProperty가 전달되지 않았다");
        Assert.Greater(eventIndex, -1, "버퍼된 이벤트가 전달되지 않았다");
        Assert.Less(userIdIndex, eventIndex, "유저 ID가 이벤트보다 늦게 전달됐다");
        Assert.Less(propertyIndex, eventIndex, "유저 프로퍼티가 이벤트보다 늦게 전달됐다");
    });
}
