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
}
