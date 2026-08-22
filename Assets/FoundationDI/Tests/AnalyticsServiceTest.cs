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

    // 초기화와 수집상태 전파는 어느 테스트에서든 배경 소음이다. 실제로 실린 데이터만 남긴다.
    private static bool IsPayloadCall(string call) =>
        !call.StartsWith("Initialize") && !call.StartsWith("SetCollectionEnabled");

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

        // 이 테스트가 보는 것은 이벤트의 순서다. 초기화·수집상태 전파는 관심사가 아니다.
        CollectionAssert.AreEqual(
            new[] { "LogEvent:first", "LogPurchase:gem_pack", "LogEvent:second" },
            provider.Calls.Where(IsPayloadCall).ToList());
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

        var calls = provider.Calls.Where(IsPayloadCall).ToList();
        var userIdIndex = calls.IndexOf("SetUserId:player-a");
        var propertyIndex = calls.IndexOf("SetUserProperty:cohort=2026-08");
        var eventIndex = calls.IndexOf("LogEvent:tutorial_start");

        Assert.Greater(userIdIndex, -1, "SetUserId가 전달되지 않았다");
        Assert.Greater(propertyIndex, -1, "SetUserProperty가 전달되지 않았다");
        Assert.Greater(eventIndex, -1, "버퍼된 이벤트가 전달되지 않았다");
        Assert.Less(userIdIndex, eventIndex, "유저 ID가 이벤트보다 늦게 전달됐다");
        Assert.Less(propertyIndex, eventIndex, "유저 프로퍼티가 이벤트보다 늦게 전달됐다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator provider_하나가_초기화에_실패해도_초기화는_성공하고_실패한_provider에는_전달되지_않는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var failing = new FakeAnalyticsProvider("Failing") { InitializeResult = false };
        var healthy = new FakeAnalyticsProvider("Healthy");
        var sut = new AnalyticsService(new IAnalyticsProvider[] { failing, healthy }, NewOptions());

        LogAssert.Expect(LogType.Error, new Regex("Failing"));
        var ok = await sut.InitializeAsync();

        Assert.IsTrue(ok, "하나가 살아 있는데 초기화가 실패로 보고됐다");
        Assert.IsTrue(sut.IsInitialized);

        sut.LogEvent("after_init");

        Assert.AreEqual(0, failing.Events.Count, "초기화에 실패한 provider에 이벤트가 전달됐다");
        Assert.AreEqual(1, healthy.Events.Count, "살아남은 provider에 이벤트가 전달되지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 모든_provider가_초기화에_실패하면_false를_반환하고_버퍼는_유지된다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAnalyticsProvider("Only") { InitializeResult = false };
        var sut = new AnalyticsService(new IAnalyticsProvider[] { provider }, NewOptions());

        sut.LogEvent("before_init");

        LogAssert.Expect(LogType.Error, new Regex("Only"));
        var first = await sut.InitializeAsync();

        Assert.IsFalse(first);
        Assert.IsFalse(sut.IsInitialized);
        Assert.AreEqual(0, provider.Events.Count);

        // 네트워크 없이 앱을 켠 경우가 실제로 이 경로다. 한 번 실패했다고 세션 전체를 포기하지 않는다.
        provider.InitializeResult = true;
        var second = await sut.InitializeAsync();

        Assert.IsTrue(second, "재시도가 성공하지 않았다");
        Assert.AreEqual(2, provider.InitializeCount, "재시도 자체가 일어나지 않았다");
        Assert.AreEqual(1, provider.Events.Count, "버퍼가 유지되지 않아 이벤트를 잃었다");
        Assert.AreEqual("before_init", provider.Events[0].Name);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator InitializeAsync는_재진입해도_초기화를_두_번_시작하지_않는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAnalyticsProvider { DeferInitialize = true };
        var sut = new AnalyticsService(new IAnalyticsProvider[] { provider }, NewOptions());

        var first = sut.InitializeAsync();
        var second = sut.InitializeAsync();

        Assert.AreEqual(1, provider.InitializeCount, "초기화를 두 번 시작했다");

        provider.CompleteInitialize(true);

        Assert.IsTrue(await first);
        Assert.IsTrue(await second, "편승한 호출자가 결과를 받지 못했다");
        Assert.AreEqual(1, provider.InitializeCount);

        // 이미 초기화된 뒤의 호출은 즉시 true다.
        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, provider.InitializeCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator CollectionEnabled가_false면_어떤_provider에도_전달되지_않는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = new AnalyticsService(new IAnalyticsProvider[] { provider },
                                       NewOptions(collectionEnabled: false));

        sut.LogEvent("before_init");
        sut.SetUserId("player-a");

        await sut.InitializeAsync();

        sut.LogEvent("after_init");
        sut.SetUserProperty("cohort", "2026-08");
        sut.LogPurchase(new PurchaseInfo("gem_pack", 4.99, "USD"));

        Assert.AreEqual(0, provider.Events.Count, "수집이 꺼졌는데 이벤트가 전달됐다");
        Assert.AreEqual(0, provider.UserIds.Count, "수집이 꺼졌는데 유저 ID가 전달됐다");
        Assert.AreEqual(0, provider.Properties.Count, "수집이 꺼졌는데 유저 프로퍼티가 전달됐다");
        Assert.AreEqual(0, provider.Purchases.Count, "수집이 꺼졌는데 구매가 전달됐다");

        // SDK는 우리가 LogEvent를 부르지 않아도 세션·화면 이벤트를 자동 수집한다.
        // 초기 수집 상태를 전파하지 않으면 게이트가 사실상 무력하다.
        CollectionAssert.AreEqual(new[] { false }, provider.CollectionFlags,
                                  "초기 수집 상태가 provider에 전파되지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator CollectionEnabled를_바꾸면_모든_provider에_전파되고_같은_값_재설정은_전파되지_않는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var a = new FakeAnalyticsProvider("A");
        var b = new FakeAnalyticsProvider("B");
        var sut = new AnalyticsService(new IAnalyticsProvider[] { a, b }, NewOptions());

        await sut.InitializeAsync();

        CollectionAssert.AreEqual(new[] { true }, a.CollectionFlags);

        sut.CollectionEnabled = false;
        sut.CollectionEnabled = false;
        sut.CollectionEnabled = true;

        CollectionAssert.AreEqual(new[] { true, false, true }, a.CollectionFlags);
        CollectionAssert.AreEqual(new[] { true, false, true }, b.CollectionFlags);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_모든_provider가_Dispose되고_이후_호출은_무시된다() =>
        UniTask.ToCoroutine(async () =>
    {
        var a = new FakeAnalyticsProvider("A");
        var b = new FakeAnalyticsProvider("B");
        var sut = new AnalyticsService(new IAnalyticsProvider[] { a, b }, NewOptions());

        await sut.InitializeAsync();

        sut.Dispose();
        sut.Dispose();

        Assert.AreEqual(1, a.DisposeCount, "A가 해제되지 않았거나 중복 해제됐다");
        Assert.AreEqual(1, b.DisposeCount, "B가 해제되지 않았거나 중복 해제됐다");

        sut.LogEvent("after_dispose");
        sut.SetUserId("player-a");
        sut.CollectionEnabled = false;

        Assert.AreEqual(0, a.Events.Count, "해제 후 이벤트가 전달됐다");
        Assert.AreEqual(0, a.UserIds.Count, "해제 후 유저 ID가 전달됐다");
        Assert.IsFalse(await sut.InitializeAsync(), "해제 후 초기화가 성공으로 보고됐다");
    });
}
