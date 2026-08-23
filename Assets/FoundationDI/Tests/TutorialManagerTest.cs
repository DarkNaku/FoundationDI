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
}
