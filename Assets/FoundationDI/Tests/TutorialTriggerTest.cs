using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public struct TutorialTestLevelStartedMessage
{
    public int Level;
}

public sealed class TutorialTestLevel3Trigger : MessageTrigger<TutorialTestLevelStartedMessage>
{
    protected override bool Match(TutorialTestLevelStartedMessage message) => message.Level == 3;
}

public sealed class TutorialTestAnyLevelTrigger : MessageTrigger<TutorialTestLevelStartedMessage>
{
}

public class TutorialTriggerTest
{
    private MessageService _message;
    private FakeTargetRegistry _targets;

    [SetUp]
    public void SetUp()
    {
        _message = new MessageService();
        _targets = new FakeTargetRegistry();
    }

    [TearDown]
    public void TearDown() => _message.Dispose();

    private TutorialTriggerContext Context => new(_message, _targets);

    [Test]
    public void Auto트리거는_Arm_즉시_발동한다()
    {
        var sut = new AutoTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        Assert.AreEqual(1, fired);
    }

    [Test]
    public void Manual트리거는_같은_ID로_Fire해야_발동한다()
    {
        var sut = new ManualTrigger("tutorialtest.move");
        var fired = 0;

        sut.Arm(Context, () => fired++);

        try
        {
            Assert.IsFalse(ManualTrigger.Fire("tutorialtest.jump"));
            Assert.AreEqual(0, fired);

            Assert.IsTrue(ManualTrigger.Fire("tutorialtest.move"));
            Assert.AreEqual(1, fired);
        }
        finally
        {
            sut.Disarm();
        }
    }

    [Test]
    public void Manual트리거는_Disarm되면_발동하지_않는다()
    {
        var sut = new ManualTrigger("tutorialtest.move");
        var fired = 0;

        sut.Arm(Context, () => fired++);
        sut.Disarm();

        Assert.IsFalse(ManualTrigger.Fire("tutorialtest.move"));
        Assert.AreEqual(0, fired);
    }

    [Test]
    public void Message트리거는_Match를_통과한_메시지에만_발동한다()
    {
        var sut = new TutorialTestLevel3Trigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        try
        {
            _message.Publish(new TutorialTestLevelStartedMessage { Level = 1 });
            Assert.AreEqual(0, fired);

            _message.Publish(new TutorialTestLevelStartedMessage { Level = 3 });
            Assert.AreEqual(1, fired);
        }
        finally
        {
            sut.Disarm();
        }
    }

    [Test]
    public void Message트리거는_Match를_오버라이드하지_않으면_모든_메시지에_발동한다()
    {
        var sut = new TutorialTestAnyLevelTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        try
        {
            _message.Publish(new TutorialTestLevelStartedMessage { Level = 1 });

            Assert.AreEqual(1, fired);
        }
        finally
        {
            sut.Disarm();
        }
    }

    [Test]
    public void Message트리거는_Disarm하면_구독이_해제된다()
    {
        var sut = new TutorialTestAnyLevelTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);
        sut.Disarm();

        _message.Publish(new TutorialTestLevelStartedMessage { Level = 1 });

        Assert.AreEqual(0, fired);
    }

    [Test]
    public void Message트리거는_한번_발동한_뒤_다시_발동하지_않는다()
    {
        var sut = new TutorialTestAnyLevelTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        try
        {
            _message.Publish(new TutorialTestLevelStartedMessage { Level = 1 });
            _message.Publish(new TutorialTestLevelStartedMessage { Level = 2 });

            Assert.AreEqual(1, fired);
        }
        finally
        {
            sut.Disarm();
        }
    }

    [Test]
    public void ButtonClick트리거는_타깃_버튼을_누르면_발동한다()
    {
        var go = new GameObject("button", typeof(RectTransform), typeof(Button));
        var button = go.GetComponent<Button>();
        _targets.Register("shop.buy", go.transform);

        var sut = new ButtonClickTrigger(TutorialTargetRef.FromKey("shop.buy"));
        var fired = 0;

        try
        {
            sut.Arm(Context, () => fired++);

            button.onClick.Invoke();

            Assert.AreEqual(1, fired);
        }
        finally
        {
            sut.Disarm();
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ButtonClick트리거는_Disarm하면_리스너가_제거된다()
    {
        var go = new GameObject("button", typeof(RectTransform), typeof(Button));
        var button = go.GetComponent<Button>();
        _targets.Register("shop.buy", go.transform);

        var sut = new ButtonClickTrigger(TutorialTargetRef.FromKey("shop.buy"));
        var fired = 0;

        try
        {
            sut.Arm(Context, () => fired++);
            sut.Disarm();

            button.onClick.Invoke();

            Assert.AreEqual(0, fired);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ButtonClick트리거는_타깃이_없으면_발동하지_않고_예외도_없다()
    {
        var sut = new ButtonClickTrigger(TutorialTargetRef.FromKey("missing"));
        var fired = 0;

        Assert.DoesNotThrow(() => sut.Arm(Context, () => fired++));
        Assert.AreEqual(0, fired);

        sut.Disarm();
    }
}
