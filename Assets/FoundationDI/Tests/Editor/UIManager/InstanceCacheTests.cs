using System;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class InstanceCacheTests
{
    private class FakeA { }

    [Test]
    public void 등록한_인스턴스를_타입으로_꺼낸다()
    {
        var cache = new InstanceCache();
        var a = new FakeA();
        cache.Register(typeof(FakeA), a);

        Assert.IsTrue(cache.TryGet(typeof(FakeA), out var got));
        Assert.AreSame(a, got);
    }

    [Test]
    public void 꺼낸_인스턴스는_캐시에서_제거된다()
    {
        var cache = new InstanceCache();
        cache.Register(typeof(FakeA), new FakeA());
        cache.TryGet(typeof(FakeA), out _);

        Assert.IsFalse(cache.TryGet(typeof(FakeA), out _));
    }
}
