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
}
