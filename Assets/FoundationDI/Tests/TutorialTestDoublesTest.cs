using System.Collections;
using System.Threading;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TutorialTestDoublesTest
{
    [Test]
    public void 가짜트리거는_Arm되기_전에_발동해도_아무일이_없다()
    {
        var sut = new FakeTrigger();

        Assert.DoesNotThrow(() => sut.Fire());
        Assert.AreEqual(0, sut.ArmCount);
    }

    [Test]
    public void 가짜트리거는_Arm_후_Fire하면_콜백을_부른다()
    {
        var sut = new FakeTrigger();
        var fired = 0;

        sut.Arm(default, () => fired++);
        sut.Fire();

        Assert.AreEqual(1, sut.ArmCount);
        Assert.AreEqual(1, fired);
        Assert.IsTrue(sut.IsArmed);
    }

    [Test]
    public void 가짜트리거는_Disarm되면_Fire해도_콜백을_부르지_않는다()
    {
        var sut = new FakeTrigger();
        var fired = 0;

        sut.Arm(default, () => fired++);
        sut.Disarm();
        sut.Fire();

        Assert.AreEqual(0, fired);
        Assert.AreEqual(1, sut.DisarmCount);
        Assert.IsFalse(sut.IsArmed);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 가짜시계는_대기를_즉시_끝낸다() => AwaitableTest.Run(async () =>
    {
        var sut = new FakeClock();

        await sut.DelayAsync(10f, CancellationToken.None);

        Assert.AreEqual(10f, sut.TotalDelay);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 가짜모듈은_Show와_Hide_횟수를_센다() => AwaitableTest.Run(async () =>
    {
        var sut = new FakeModule();

        await sut.ShowAsync(null, CancellationToken.None);
        Assert.AreEqual(1, sut.ShowCount);

        await sut.HideAsync(CancellationToken.None);
        Assert.AreEqual(1, sut.HideCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 가짜레지스트리는_등록된_타깃을_즉시_돌려준다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = new FakeTargetRegistry();
        sut.Register("shop.buy", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("shop.buy"), 0f,
                                            CancellationToken.None);

        Assert.IsNotNull(handle);
        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [Test]
    public void 타깃핸들은_대상이_바뀌면_Changed를_쏜다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = new TutorialTargetHandle(a.transform);
        Transform observed = null;

        try
        {
            sut.Changed += t => observed = t;
            sut.SetCurrent(b.transform);

            Assert.AreSame(b.transform, observed);
            Assert.AreSame(b.transform, sut.Current);
        }
        finally
        {
            sut.Dispose();
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void 타깃핸들은_같은_대상으로_다시_설정하면_Changed를_쏘지_않는다()
    {
        var a = new GameObject("a");
        var sut = new TutorialTargetHandle(a.transform);
        var count = 0;

        try
        {
            sut.Changed += _ => count++;
            sut.SetCurrent(a.transform);

            Assert.AreEqual(0, count);
        }
        finally
        {
            sut.Dispose();
            Object.DestroyImmediate(a);
        }
    }

    [Test]
    public void 타깃핸들은_대상이_파괴되면_Current가_null이다()
    {
        var a = new GameObject("a");
        var sut = new TutorialTargetHandle(a.transform);

        Object.DestroyImmediate(a);

        Assert.IsNull(sut.Current);

        sut.Dispose();
    }

    [Test]
    public void 타깃핸들은_Dispose_후_Changed를_쏘지_않는다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = new TutorialTargetHandle(a.transform);
        var count = 0;

        try
        {
            sut.Changed += _ => count++;
            sut.Dispose();
            sut.SetCurrent(b.transform);

            Assert.AreEqual(0, count);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }
}
