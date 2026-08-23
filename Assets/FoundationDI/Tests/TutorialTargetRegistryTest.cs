using System.Collections;
using System.Threading;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TutorialTargetRegistryTest
{
    private static TutorialTargetRegistry NewRegistry() =>
        new TutorialTargetRegistry(new FakeClock());

    [Test]
    public void 직접참조는_등록없이_해석된다()
    {
        var go = new GameObject("target");
        var sut = NewRegistry();

        try
        {
            Assert.IsTrue(sut.TryResolve(TutorialTargetRef.FromTransform(go.transform), out var t));
            Assert.AreSame(go.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 등록되지_않은_키는_해석되지_않는다()
    {
        var sut = NewRegistry();

        Assert.IsFalse(sut.TryResolve(TutorialTargetRef.FromKey("missing"), out var t));
        Assert.IsNull(t);
    }

    [Test]
    public void 등록한_키가_해석된다()
    {
        var go = new GameObject("target");
        var sut = NewRegistry();

        try
        {
            sut.Register("shop.buy", go.transform);

            Assert.IsTrue(sut.TryResolve(TutorialTargetRef.FromKey("shop.buy"), out var t));
            Assert.AreSame(go.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 같은_키를_두번_등록하면_마지막_등록이_이긴다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = NewRegistry();

        try
        {
            sut.Register("k", a.transform);
            sut.Register("k", b.transform);

            sut.TryResolve(TutorialTargetRef.FromKey("k"), out var t);

            Assert.AreSame(b.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void 마지막_등록을_해제하면_이전_등록으로_돌아간다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = NewRegistry();

        try
        {
            sut.Register("k", a.transform);
            sut.Register("k", b.transform);
            sut.Unregister("k", b.transform);

            sut.TryResolve(TutorialTargetRef.FromKey("k"), out var t);

            Assert.AreSame(a.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_등록된_타깃은_즉시_해석된다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);

        Assert.IsNotNull(handle);
        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 나중에_등록되는_타깃을_기다린다() => AwaitableTest.Run(async () =>
    {
        var sut = NewRegistry();
        TutorialTargetHandle handle = null;

        async void Resolve()
        {
            handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);
        }

        Resolve();

        Assert.IsNull(handle);

        var go = new GameObject("target");
        sut.Register("k", go.transform);

        await AwaitableTest.WaitUntil(() => handle != null);

        Assert.IsNotNull(handle);
        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 해석된_핸들은_타깃이_해제되면_null이_된다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);

        Assert.AreSame(go.transform, handle.Current);

        sut.Unregister("k", go.transform);

        Assert.IsNull(handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 해석된_핸들은_타깃이_다시_등록되면_복귀한다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);

        sut.Unregister("k", go.transform);

        Assert.IsNull(handle.Current);

        sut.Register("k", go.transform);

        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 핸들을_Dispose하면_등록해도_영향받지_않는다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);
        var changed = 0;

        handle.Changed += _ => changed++;
        handle.Dispose();

        sut.Unregister("k", go.transform);
        sut.Register("k", go.transform);

        Assert.AreEqual(0, changed);

        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 타임아웃이_지나면_null을_돌려준다() => AwaitableTest.Run(async () =>
    {
        var sut = NewRegistry();

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("missing"), 0.05f,
                                            CancellationToken.None);

        Assert.IsNull(handle);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 빈_참조는_null_대상의_핸들을_즉시_돌려준다() => AwaitableTest.Run(async () =>
    {
        var sut = NewRegistry();

        var handle = await sut.ResolveAsync(default, 0f, CancellationToken.None);

        Assert.IsNotNull(handle);
        Assert.IsNull(handle.Current);

        handle.Dispose();
    });
}
