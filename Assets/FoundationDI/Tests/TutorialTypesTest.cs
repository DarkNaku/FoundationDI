using System.Collections;
using System.Threading;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TutorialTypesTest
{
    private static TutorialStep NewStep(string id = "step",
                                        ITutorialTrigger start = null,
                                        ITutorialTrigger end = null)
    {
        return new TutorialStep(id, start ?? new FakeTrigger(), end ?? new FakeTrigger(),
                                new ITutorialModule[0], default, 0f, 0f);
    }

    [Test]
    public void 타깃참조가_비어있으면_IsEmpty가_참이다()
    {
        var sut = default(TutorialTargetRef);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 키만_채우면_HasKey가_참이고_비어있지_않다()
    {
        var sut = TutorialTargetRef.FromKey("shop.buy");

        Assert.IsFalse(sut.IsEmpty);
        Assert.IsTrue(sut.HasKey);
        Assert.AreEqual("shop.buy", sut.Key);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 공백문자열_키는_키가_없는_것으로_본다()
    {
        var sut = TutorialTargetRef.FromKey("   ");

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 직접참조를_채우면_비어있지_않고_키는_없다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.FromTransform(go.transform);

            Assert.IsFalse(sut.IsEmpty);
            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 직접참조가_파괴되면_다시_비어있는_것으로_본다()
    {
        var go = new GameObject("target");
        var sut = TutorialTargetRef.FromTransform(go.transform);

        Object.DestroyImmediate(go);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 직접참조가_키보다_우선한다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.Create(go.transform, "shop.buy");

            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 스텝은_트리거가_없으면_Auto로_채운다()
    {
        var sut = new TutorialStep("step", null, null, null, default, 0f, 0f);

        Assert.IsInstanceOf<AutoTrigger>(sut.StartTrigger);
        Assert.IsInstanceOf<AutoTrigger>(sut.EndTrigger);
        Assert.IsNotNull(sut.Modules);
        Assert.AreEqual(0, sut.Modules.Count);
    }

    [Test]
    public void 스텝은_음수_지연을_0으로_보정한다()
    {
        var sut = new TutorialStep("step", null, null, null, default, -1f, -2f);

        Assert.AreEqual(0f, sut.StartDelay);
        Assert.AreEqual(0f, sut.EndDelay);
    }

    [Test]
    public void 스텝은_null_모듈을_걸러낸다()
    {
        var modules = new ITutorialModule[] { new FakeModule(), null, new FakeModule() };

        var sut = new TutorialStep("step", null, null, modules, default, 0f, 0f);

        Assert.AreEqual(2, sut.Modules.Count);
    }

    [Test]
    public void 시퀀스는_스텝이_없으면_빈_목록을_갖는다()
    {
        var sut = new TutorialSequence("intro", null, null);

        Assert.IsNotNull(sut.Steps);
        Assert.AreEqual(0, sut.Steps.Count);
        Assert.IsInstanceOf<AutoTrigger>(sut.StartTrigger);
        Assert.AreEqual(ResumeMode.RestartSequence, sut.ResumeMode);
    }

    [Test]
    public void 시퀀스는_null_스텝을_걸러낸다()
    {
        var steps = new[] { NewStep("a"), null, NewStep("b") };

        var sut = new TutorialSequence("intro", null, steps);

        Assert.AreEqual(2, sut.Steps.Count);
        Assert.AreEqual("a", sut.Steps[0].Id);
        Assert.AreEqual("b", sut.Steps[1].Id);
    }

    [Test]
    public void 시퀀스는_음수_타임아웃을_0으로_보정한다()
    {
        var sut = new TutorialSequence("intro", null, null, 0, ResumeMode.RestartSequence, -5f);

        Assert.AreEqual(0f, sut.TargetTimeout);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트리거어웨이터는_발동하면_완료된다() => AwaitableTest.Run(async () =>
    {
        var trigger = new FakeTrigger();
        var done = false;

        async void Wait()
        {
            await TutorialTriggerAwaiter.WaitAsync(trigger, default, CancellationToken.None);
            done = true;
        }

        Wait();

        Assert.IsFalse(done);
        Assert.AreEqual(1, trigger.ArmCount);

        trigger.Fire();

        await AwaitableTest.WaitUntil(() => done);

        Assert.IsTrue(done);
        Assert.AreEqual(1, trigger.DisarmCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트리거어웨이터는_취소되면_Disarm하고_취소예외를_던진다() => AwaitableTest.Run(async () =>
    {
        var trigger = new FakeTrigger();
        var cts = new CancellationTokenSource();
        var cancelled = false;

        async void Wait()
        {
            try
            {
                await TutorialTriggerAwaiter.WaitAsync(trigger, default, cts.Token);
            }
            catch (System.OperationCanceledException)
            {
                cancelled = true;
            }
        }

        Wait();
        cts.Cancel();

        await AwaitableTest.WaitUntil(() => cancelled);

        Assert.IsTrue(cancelled);
        Assert.AreEqual(1, trigger.DisarmCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트리거어웨이터는_두번_발동해도_한번만_완료된다() => AwaitableTest.Run(async () =>
    {
        var trigger = new FakeTrigger();
        var done = 0;

        async void Wait()
        {
            await TutorialTriggerAwaiter.WaitAsync(trigger, default, CancellationToken.None);
            done++;
        }

        Wait();

        // 첫 Fire에서 Disarm되므로 두 번째 Fire는 아무 일도 하지 않아야 한다.
        trigger.Fire();
        trigger.Fire();

        await AwaitableTest.WaitUntil(() => done > 0);

        Assert.AreEqual(1, done);
    });
}
