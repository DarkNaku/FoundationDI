using System.Linq;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AnalyticsServiceTest
{
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
}
