using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class MessageServiceTest
{
    // 값 타입/참조 타입 모두 메시지가 될 수 있어야 하므로 둘 다 준비한다.
    private struct ScoreChanged
    {
        public int Value;
    }

    private struct LevelUp
    {
        public int Level;
    }

    private class PlayerDied
    {
        public string Reason;
    }

    private MessageService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new MessageService();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
        _service = null;
    }

    [Test]
    public void 구독한_핸들러가_발행된_메시지를_받고_다른_타입은_받지_않는다()
    {
        var received = new List<int>();
        var otherCalled = false;

        _service.Subscribe<ScoreChanged>(m => received.Add(m.Value));
        _service.Subscribe<LevelUp>(_ => otherCalled = true);

        _service.Publish(new ScoreChanged { Value = 10 });

        Assert.AreEqual(new[] { 10 }, received.ToArray());
        Assert.IsFalse(otherCalled, "다른 타입의 구독자가 호출되면 안 된다.");
    }

    [Test]
    public void 참조_타입_메시지도_발행하고_수신할_수_있다()
    {
        PlayerDied received = null;

        _service.Subscribe<PlayerDied>(m => received = m);
        _service.Publish(new PlayerDied { Reason = "fall" });

        Assert.IsNotNull(received);
        Assert.AreEqual("fall", received.Reason);
    }

    [Test]
    public void 같은_타입에_여러_핸들러를_구독하면_모두_호출된다()
    {
        var calls = new List<string>();

        _service.Subscribe<ScoreChanged>(_ => calls.Add("a"));
        _service.Subscribe<ScoreChanged>(_ => calls.Add("b"));
        _service.Subscribe<ScoreChanged>(_ => calls.Add("c"));

        _service.Publish(new ScoreChanged { Value = 1 });

        Assert.AreEqual(new[] { "a", "b", "c" }, calls.ToArray());
    }

    [Test]
    public void 같은_핸들러를_두_번_구독하면_두_번_호출되고_하나만_해제하면_한_번_호출된다()
    {
        var count = 0;
        Action<ScoreChanged> handler = _ => count++;

        var first = _service.Subscribe(handler);
        _service.Subscribe(handler);

        _service.Publish(new ScoreChanged());
        Assert.AreEqual(2, count);

        count = 0;
        first.Dispose();

        _service.Publish(new ScoreChanged());
        Assert.AreEqual(1, count);
    }

    [Test]
    public void 구독을_Dispose하면_더_이상_수신하지_않고_중복_Dispose도_안전하다()
    {
        var count = 0;
        var subscription = _service.Subscribe<ScoreChanged>(_ => count++);

        _service.Publish(new ScoreChanged());
        Assert.AreEqual(1, count);

        subscription.Dispose();
        Assert.DoesNotThrow(() => subscription.Dispose());

        _service.Publish(new ScoreChanged());
        Assert.AreEqual(1, count, "해제 후에는 더 이상 수신하면 안 된다.");
    }

    [Test]
    public void 구독자가_없는_타입을_발행해도_예외를_던지지_않는다()
    {
        Assert.DoesNotThrow(() => _service.Publish(new ScoreChanged { Value = 1 }));

        // 마지막 구독자가 빠진 뒤에도 마찬가지다.
        _service.Subscribe<LevelUp>(_ => { }).Dispose();
        Assert.DoesNotThrow(() => _service.Publish(new LevelUp { Level = 2 }));
    }

    [Test]
    public void 발행_중_다른_구독을_해제해도_현재_발행은_완주한다()
    {
        var calls = new List<string>();
        IDisposable second = null;

        _service.Subscribe<ScoreChanged>(_ =>
        {
            calls.Add("first");
            second.Dispose();
        });

        second = _service.Subscribe<ScoreChanged>(_ => calls.Add("second"));

        _service.Publish(new ScoreChanged());

        Assert.AreEqual(new[] { "first", "second" }, calls.ToArray(),
            "발행은 시작 시점의 스냅샷으로 완주해야 한다.");

        calls.Clear();
        _service.Publish(new ScoreChanged());

        Assert.AreEqual(new[] { "first" }, calls.ToArray(), "다음 발행부터는 해제가 반영돼야 한다.");
    }

    [Test]
    public void 발행_중_추가한_구독은_다음_발행부터_호출된다()
    {
        var calls = new List<string>();
        var subscribed = false;

        _service.Subscribe<ScoreChanged>(_ =>
        {
            calls.Add("first");

            if (subscribed) return;

            subscribed = true;
            _service.Subscribe<ScoreChanged>(__ => calls.Add("late"));
        });

        _service.Publish(new ScoreChanged());
        Assert.AreEqual(new[] { "first" }, calls.ToArray());

        calls.Clear();
        _service.Publish(new ScoreChanged());
        Assert.AreEqual(new[] { "first", "late" }, calls.ToArray());
    }

    [Test]
    public void 핸들러가_예외를_던져도_나머지_핸들러가_호출된다()
    {
        LogAssert.Expect(LogType.Exception, new Regex("boom"));

        var calls = new List<string>();

        _service.Subscribe<ScoreChanged>(_ => calls.Add("before"));
        _service.Subscribe<ScoreChanged>(_ => throw new InvalidOperationException("boom"));
        _service.Subscribe<ScoreChanged>(_ => calls.Add("after"));

        Assert.DoesNotThrow(() => _service.Publish(new ScoreChanged()),
            "핸들러 예외는 발행자에게 전파되지 않고 로그로 격리돼야 한다.");

        Assert.AreEqual(new[] { "before", "after" }, calls.ToArray());
    }

    [Test]
    public void 서비스를_Dispose하면_모든_구독이_해제되고_이후_사용은_거부된다()
    {
        var count = 0;
        var subscription = _service.Subscribe<ScoreChanged>(_ => count++);

        _service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _service.Publish(new ScoreChanged()));
        Assert.Throws<ObjectDisposedException>(() => _service.Subscribe<ScoreChanged>(_ => count++));
        Assert.AreEqual(0, count);

        // 남아 있던 구독 토큰의 Dispose와 서비스 중복 Dispose는 조용히 넘어가야 한다.
        Assert.DoesNotThrow(() => subscription.Dispose());
        Assert.DoesNotThrow(() => _service.Dispose());
    }

    [Test]
    public void null_핸들러_구독은_거부된다()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Subscribe<ScoreChanged>(null));
    }

    [Test]
    public void RegisterMessageService로_등록하면_IMessageService를_싱글턴으로_해석할_수_있다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();

        var container = builder.Build();

        var service = container.Resolve<IMessageService>();

        Assert.IsNotNull(service);
        Assert.IsInstanceOf<MessageService>(service);
        Assert.AreSame(service, container.Resolve<IMessageService>());

        Assert.DoesNotThrow(() => container.Dispose());
    }
}
