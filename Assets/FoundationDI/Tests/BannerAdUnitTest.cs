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

    [Test]
    public void 광고제거_상태에서는_배너를_표시하지_않고_어댑터도_만들지_않는다()
    {
        // 어댑터를 만들면 SDK가 배너를 요청하고 임프레션이 발생해 수익 리포트가 오염된다.
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => true);

        sut.Show();

        Assert.AreEqual(0, factory.Created.Count, "광고제거 상태인데 어댑터를 만들었다");
        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
    }

    [Test]
    public void 광고제거가_켜지면_표시중인_배너를_해제하고_높이를_0으로_알린다()
    {
        var adsRemoved = false;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();
        factory.Last.SetHeight(120f);
        var first = factory.Last;

        var reported = -1f;
        sut.HeightChanged += h => reported = h;

        adsRemoved = true;
        sut.OnAdsRemovedChanged(true);

        Assert.IsTrue(first.IsDisposed, "배너 어댑터가 해제되지 않았다");
        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
        Assert.AreEqual(0f, reported, 0.001f);
    }

    [Test]
    public void 광고제거가_해제되면_원래_표시중이던_배너를_다시_띄운다()
    {
        var adsRemoved = false;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();
        adsRemoved = true;
        sut.OnAdsRemovedChanged(true);

        adsRemoved = false;
        sut.OnAdsRemovedChanged(false);

        Assert.AreEqual(2, factory.Created.Count, "배너가 복구되지 않았다");
        Assert.IsTrue(sut.IsVisible);
        Assert.AreEqual(1, factory.Last.ShowCount);
    }

    [Test]
    public void 숨긴_상태에서_광고제거가_해제돼도_배너를_띄우지_않는다()
    {
        var adsRemoved = false;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();
        sut.Hide();                       // 게임이 명시적으로 숨겼다
        adsRemoved = true;
        sut.OnAdsRemovedChanged(true);

        adsRemoved = false;
        sut.OnAdsRemovedChanged(false);

        Assert.IsFalse(sut.IsVisible, "게임이 숨긴 배너가 멋대로 복구됐다");
    }

    [Test]
    public void 광고제거_상태에서_Show를_호출하면_의도만_기록했다가_해제되면_배너가_나타난다()
    {
        var adsRemoved = true;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();

        Assert.AreEqual(0, factory.Created.Count, "광고제거 상태인데 어댑터를 만들었다");
        Assert.IsFalse(sut.IsVisible);

        adsRemoved = false;
        sut.OnAdsRemovedChanged(false);

        Assert.AreEqual(1, factory.Created.Count, "광고제거 해제 후 배너가 나타나지 않았다");
        Assert.AreEqual(1, factory.Last.ShowCount);
        Assert.IsTrue(sut.IsVisible);
    }

    [Test]
    public void 숨긴_뒤_어댑터가_스스로_높이를_바꿔도_0으로_중계된다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        factory.Last.SetHeight(120f);
        sut.Hide();

        var reported = -1f;
        sut.HeightChanged += h => reported = h;
        factory.Last.SetHeight(90f); // SDK가 숨긴 뒤에도 스스로 배너를 갱신하는 상황 재현

        Assert.AreEqual(0f, reported, 0.001f, "숨긴 상태에서 어댑터의 높이 변경이 그대로 중계됐다");
        Assert.AreEqual(0f, sut.Height, 0.001f);
    }

    [Test]
    public void 파괴하면_현재_어댑터를_해제하고_비표시_상태가_된다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        var adapter = factory.Last;

        sut.Dispose();

        Assert.IsTrue(adapter.IsDisposed, "Dispose가 어댑터를 해제하지 않았다");
        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
    }

    [Test]
    public void Dispose를_두번_호출해도_어댑터는_한번만_해제된다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        var adapter = factory.Last;

        Assert.DoesNotThrow(() =>
        {
            sut.Dispose();
            sut.Dispose();
        });

        Assert.IsTrue(adapter.IsDisposed);
        Assert.AreEqual(1, factory.Created.Count, "두번째 Dispose에서 어댑터가 다시 만들어졌다");
    }

    [Test]
    public void 파괴된_뒤_Show를_호출해도_새_어댑터를_만들지_않는다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        sut.Dispose();

        sut.Show();

        Assert.AreEqual(1, factory.Created.Count, "Dispose 이후 Show가 새 어댑터를 만들었다");
        Assert.IsFalse(sut.IsVisible);
    }

    [Test]
    public void 파괴된_뒤에는_Hide와_Destroy가_조용하다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        sut.Dispose();

        var invoked = false;
        sut.HeightChanged += _ => invoked = true;

        sut.Hide();
        sut.Destroy();

        Assert.IsFalse(invoked, "Dispose 이후 Hide/Destroy가 HeightChanged를 발화시켰다");
    }
}
