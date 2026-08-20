using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdTypesTest
{
    [Test]
    public void 재시도_지연은_시도횟수에_대해_지수적으로_증가한다()
    {
        var policy = new AdRetryPolicy(maxAttempts: 5, baseSeconds: 2f, maxDelaySeconds: 64f);

        Assert.AreEqual(2f, policy.DelayFor(1), 0.001f);
        Assert.AreEqual(4f, policy.DelayFor(2), 0.001f);
        Assert.AreEqual(8f, policy.DelayFor(3), 0.001f);
    }
}
