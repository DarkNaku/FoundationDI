using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class BannerAdUnitTest
{
    // 생성된 어댑터를 전부 기록해서 Destroy 후 재생성을 검증할 수 있게 한다.
    private class AdapterFactory
    {
        public readonly List<FakeBannerAdapter> Created = new();
        public FakeBannerAdapter Last => Created.Count > 0 ? Created[^1] : null;

        public IBannerAdapter Create()
        {
            var adapter = new FakeBannerAdapter();
            Created.Add(adapter);
            return adapter;
        }
    }

    [Test]
    public void 배너를_표시하면_어댑터를_만들어_Show를_호출하고_높이를_보고한다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();

        Assert.AreEqual(1, factory.Created.Count, "어댑터가 생성되지 않았다");
        Assert.AreEqual(1, factory.Last.ShowCount);
        Assert.IsTrue(sut.IsVisible);

        var reported = -1f;
        sut.HeightChanged += h => reported = h;
        factory.Last.SetHeight(120f);

        Assert.AreEqual(120f, sut.Height, 0.001f);
        Assert.AreEqual(120f, reported, 0.001f, "HeightChanged가 중계되지 않았다");
    }

    [Test]
    public void 배너를_숨기면_어댑터를_유지한_채_높이를_0으로_보고한다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        factory.Last.SetHeight(120f);

        var reported = -1f;
        sut.HeightChanged += h => reported = h;
        sut.Hide();

        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
        Assert.AreEqual(0f, reported, 0.001f);
        Assert.AreEqual(1, factory.Last.HideCount);
        Assert.IsFalse(factory.Last.IsDisposed, "Hide가 어댑터를 파괴했다");
        Assert.AreEqual(1, factory.Created.Count);
    }

    [Test]
    public void 배너를_파괴하면_어댑터를_해제하고_다음_표시에서_새로_만든다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        var first = factory.Last;

        sut.Destroy();

        Assert.IsTrue(first.IsDisposed, "어댑터가 해제되지 않았다");
        Assert.AreEqual(0f, sut.Height, 0.001f);

        sut.Show();

        Assert.AreEqual(2, factory.Created.Count, "파괴 후 어댑터를 새로 만들지 않았다");
        Assert.AreNotSame(first, factory.Last);
        Assert.AreEqual(1, factory.Last.ShowCount);
        Assert.IsTrue(sut.IsVisible);
    }

    [Test]
    public void 배너_임프레션은_그대로_중계된다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);
        AdImpression? received = null;
        sut.Paid += imp => received = imp;

        sut.Show();
        factory.Last.RaisePaid(new AdImpression(AdFormat.Banner, "Dummy", "TestNetwork", "banner-unit",
                                                "inst", null, 0.004, "USD",
                                                AdRevenuePrecision.Estimated, "creative-1"));

        Assert.IsTrue(received.HasValue, "임프레션이 중계되지 않았다");
        Assert.AreEqual("TestNetwork", received.Value.NetworkName);
        Assert.AreEqual(0.004, received.Value.Revenue, 0.0001);
    }
}
