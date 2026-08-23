using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class TutorialManagerTest
{
    private FakeProgressStorage _storage;
    private FakeTargetRegistry _targets;
    private FakeClock _clock;
    private MessageService _message;

    [SetUp]
    public void SetUp()
    {
        _storage = new FakeProgressStorage();
        _targets = new FakeTargetRegistry();
        _clock = new FakeClock();
        _message = new MessageService();
    }

    [TearDown]
    public void TearDown() => _message.Dispose();

    private TutorialManager NewManager() =>
        new TutorialManager(_message, _targets, _storage, _clock);

    private static TutorialStep NewStep(string id,
                                        ITutorialTrigger start,
                                        ITutorialTrigger end,
                                        params ITutorialModule[] modules)
    {
        return new TutorialStep(id, start, end, modules, default, 0f, 0f);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 시작트리거가_발동해야_시퀀스가_시작된다() => AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var stepEnd = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), stepEnd, module) }));

        Assert.AreEqual(0, module.ShowCount);
        Assert.IsFalse(sut.IsRunning);
        Assert.AreEqual(1, gate.ArmCount);

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        Assert.AreEqual(1, module.ShowCount);
        Assert.IsTrue(sut.IsRunning);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 스텝이_시작트리거_모듈Show_종료트리거_모듈Hide_순서로_진행된다() =>
        AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var module = new FakeModule { Log = log, Name = "m" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), end, module) }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        Assert.AreEqual(new[] { "m.show" }, log.ToArray());

        end.Fire();

        await AwaitableTest.WaitUntil(() => module.HideCount > 0);

        Assert.AreEqual(new[] { "m.show", "m.hide" }, log.ToArray());

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 여러_스텝이_순서대로_진행된다() => AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gate = new FakeTrigger();
        var end1 = new FakeTrigger();
        var end2 = new FakeTrigger();
        var m1 = new FakeModule { Log = log, Name = "s1" };
        var m2 = new FakeModule { Log = log, Name = "s2" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            NewStep("s1", new AutoTrigger(), end1, m1),
            NewStep("s2", new AutoTrigger(), end2, m2),
        }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => m1.ShowCount > 0);

        Assert.AreEqual(0, m2.ShowCount);

        end1.Fire();

        await AwaitableTest.WaitUntil(() => m2.ShowCount > 0);

        Assert.AreEqual(new[] { "s1.show", "s1.hide", "s2.show" }, log.ToArray());

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 시퀀스가_완료되면_Completed로_기록되고_이벤트가_발행된다() =>
        AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var started = new List<string>();
        var completed = new List<string>();
        var sut = NewManager();

        sut.SequenceStarted += id => started.Add(id);
        sut.SequenceCompleted += id => completed.Add(id);

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), end) }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => started.Count > 0);

        Assert.AreEqual(TutorialState.Running, _storage.GetState("intro"));

        end.Fire();

        await AwaitableTest.WaitUntil(() => completed.Count > 0);

        Assert.AreEqual(new[] { "intro" }, started.ToArray());
        Assert.AreEqual(new[] { "intro" }, completed.ToArray());
        Assert.AreEqual(TutorialState.Completed, _storage.GetState("intro"));
        Assert.IsTrue(sut.IsCompleted("intro"));
        Assert.IsFalse(sut.IsRunning);

        sut.Dispose();
    });

    [Test]
    public void 완료된_시퀀스는_등록해도_트리거를_arm하지_않는다()
    {
        _storage.SetState("intro", TutorialState.Completed);

        var gate = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), new FakeTrigger()) }));

        Assert.AreEqual(0, gate.ArmCount);
        Assert.IsTrue(sut.IsCompleted("intro"));

        sut.Dispose();
    }

    [Test]
    public void AllSkipped면_어떤_시퀀스도_arm하지_않는다()
    {
        _storage.AllSkipped = true;

        var gate = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), new FakeTrigger()) }));

        Assert.AreEqual(0, gate.ArmCount);
        Assert.IsTrue(sut.IsCompleted("intro"));

        sut.Dispose();
    }

    [Test]
    public void 중복_시퀀스ID는_무시된다()
    {
        var first = new FakeTrigger();
        var second = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", first, null));

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("intro"));

        sut.Register(new TutorialSequence("intro", second, null));

        Assert.AreEqual(1, first.ArmCount);
        Assert.AreEqual(0, second.ArmCount);

        sut.Dispose();
    }

    [Test]
    public void Unregister하면_트리거가_Disarm된다()
    {
        var gate = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, null));
        sut.Unregister("intro");

        Assert.AreEqual(1, gate.DisarmCount);

        sut.Dispose();
    }

    [Test]
    public void Dispose하면_대기중인_트리거가_모두_Disarm된다()
    {
        var a = new FakeTrigger();
        var b = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", a, null));
        sut.Register(new TutorialSequence("b", b, null));

        sut.Dispose();

        Assert.AreEqual(1, a.DisarmCount);
        Assert.AreEqual(1, b.DisarmCount);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 실행중_다른_시퀀스가_발동하면_대기열에_들어간다() => AwaitableTest.Run(async () =>
    {
        var gateA = new FakeTrigger();
        var gateB = new FakeTrigger();
        var endA = new FakeTrigger();
        var endB = new FakeTrigger();
        var mA = new FakeModule();
        var mB = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA,
            new[] { NewStep("s", new AutoTrigger(), endA, mA) }));
        sut.Register(new TutorialSequence("b", gateB,
            new[] { NewStep("s", new AutoTrigger(), endB, mB) }));

        gateA.Fire();

        await AwaitableTest.WaitUntil(() => mA.ShowCount > 0);

        gateB.Fire();

        Assert.AreEqual(0, mB.ShowCount);

        endA.Fire();

        await AwaitableTest.WaitUntil(() => mB.ShowCount > 0);

        Assert.AreEqual(1, mB.ShowCount);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 대기열은_Order_오름차순으로_실행된다() => AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gateA = new FakeTrigger();
        var gateHigh = new FakeTrigger();
        var gateLow = new FakeTrigger();
        var endA = new FakeTrigger();
        var mA = new FakeModule();
        var mHigh = new FakeModule { Log = log, Name = "high" };
        var mLow = new FakeModule { Log = log, Name = "low" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA,
            new[] { NewStep("s", new AutoTrigger(), endA, mA) }));
        sut.Register(new TutorialSequence("high", gateHigh,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), mHigh) }, 10));
        sut.Register(new TutorialSequence("low", gateLow,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), mLow) }, 1));

        gateA.Fire();

        await AwaitableTest.WaitUntil(() => mA.ShowCount > 0);

        gateHigh.Fire();
        gateLow.Fire();

        endA.Fire();

        await AwaitableTest.WaitUntil(() => log.Count > 0);

        Assert.AreEqual("low.show", log[0]);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 기본_재개모드는_시퀀스_처음부터_시작한다() => AwaitableTest.Run(async () =>
    {
        _storage.SetState("intro", TutorialState.Running);
        _storage.SetStepIndex("intro", 1);

        var log = new List<string>();
        var gate = new FakeTrigger();
        var m1 = new FakeModule { Log = log, Name = "s1" };
        var m2 = new FakeModule { Log = log, Name = "s2" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            NewStep("s1", new AutoTrigger(), new FakeTrigger(), m1),
            NewStep("s2", new AutoTrigger(), new FakeTrigger(), m2),
        }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => log.Count > 0);

        Assert.AreEqual("s1.show", log[0]);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator ResumeFromStep이면_저장된_스텝부터_시작한다() => AwaitableTest.Run(async () =>
    {
        _storage.SetState("intro", TutorialState.Running);
        _storage.SetStepIndex("intro", 1);

        var log = new List<string>();
        var gate = new FakeTrigger();
        var m1 = new FakeModule { Log = log, Name = "s1" };
        var m2 = new FakeModule { Log = log, Name = "s2" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            NewStep("s1", new AutoTrigger(), new FakeTrigger(), m1),
            NewStep("s2", new AutoTrigger(), new FakeTrigger(), m2),
        }, 0, ResumeMode.ResumeFromStep));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => log.Count > 0);

        Assert.AreEqual("s2.show", log[0]);
        Assert.AreEqual(0, m1.ShowCount);

        sut.Dispose();
    });

    [Test]
    public void Running_상태여도_시작트리거를_기다린다()
    {
        _storage.SetState("intro", TutorialState.Running);

        var gate = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), new FakeTrigger(), module) }));

        Assert.AreEqual(1, gate.ArmCount);
        Assert.AreEqual(0, module.ShowCount);
        Assert.IsFalse(sut.IsRunning);

        sut.Dispose();
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 스텝_지연이_시계를_통해_대기된다() => AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            new TutorialStep("s1", new AutoTrigger(), end, new[] { module }, default, 0.5f, 0.25f),
        }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        Assert.AreEqual(0.5f, _clock.TotalDelay);

        end.Fire();

        await AwaitableTest.WaitUntil(() => module.HideCount > 0);

        Assert.AreEqual(0.75f, _clock.TotalDelay);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 모듈이_예외를_던져도_다음_모듈과_스텝이_진행된다() => AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var bad = new FakeModule { Log = log, Name = "bad", ThrowOnShow = true };
        var good = new FakeModule { Log = log, Name = "good" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), end, bad, good) }));

        LogAssert.ignoreFailingMessages = true;

        try
        {
            gate.Fire();

            await AwaitableTest.WaitUntil(() => good.ShowCount > 0);

            Assert.AreEqual(1, good.ShowCount);

            end.Fire();

            await AwaitableTest.WaitUntil(() => sut.IsCompleted("intro"));

            Assert.IsTrue(sut.IsCompleted("intro"));
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            sut.Dispose();
        }
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 타깃을_못찾으면_시퀀스가_중단되고_NotStarted로_되돌아간다() =>
        AwaitableTest.Run(async () =>
    {
        _targets.FailResolve = true;

        var gate = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            new TutorialStep("s1", new AutoTrigger(), new FakeTrigger(), new[] { module },
                             TutorialTargetRef.FromKey("missing"), 0f, 0f),
        }, 0, ResumeMode.RestartSequence, 0.01f));

        LogAssert.ignoreFailingMessages = true;

        try
        {
            gate.Fire();

            await AwaitableTest.WaitUntil(
                () => _storage.GetState("intro") == TutorialState.NotStarted && !sut.IsRunning);

            Assert.AreEqual(TutorialState.NotStarted, _storage.GetState("intro"));
            Assert.AreEqual(0, module.ShowCount);
            Assert.IsFalse(sut.IsRunning);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            sut.Dispose();
        }
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Skip은_현재_시퀀스만_완료처리한다() => AwaitableTest.Run(async () =>
    {
        var gateA = new FakeTrigger();
        var gateB = new FakeTrigger();
        var mA = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), mA) }));
        sut.Register(new TutorialSequence("b", gateB, null));

        gateA.Fire();

        await AwaitableTest.WaitUntil(() => mA.ShowCount > 0);

        sut.Skip();

        await AwaitableTest.WaitUntil(() => !sut.IsRunning);

        Assert.IsTrue(sut.IsCompleted("a"));
        Assert.IsFalse(sut.IsCompleted("b"));

        sut.Dispose();
    });

    [Test]
    public void SkipAll은_AllSkipped를_세우고_모든_트리거를_Disarm한다()
    {
        var gateA = new FakeTrigger();
        var gateB = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA, null));
        sut.Register(new TutorialSequence("b", gateB, null));

        sut.SkipAll();

        Assert.IsTrue(_storage.AllSkipped);
        Assert.AreEqual(1, gateA.DisarmCount);
        Assert.AreEqual(1, gateB.DisarmCount);
        Assert.IsTrue(sut.IsCompleted("a"));
        Assert.IsTrue(sut.IsCompleted("b"));

        sut.Dispose();
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_진행중인_시퀀스가_취소되고_완료로_기록되지_않는다() =>
        AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), module) }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        sut.Dispose();

        await AwaitableTest.WaitUntil(() => !sut.IsRunning);

        Assert.AreEqual(TutorialState.Running, _storage.GetState("intro"));
        Assert.IsFalse(sut.IsCompleted("intro"));
    });
}
