using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class TutorialAuthoringTest
{
    private GameObject _root;

    [SetUp]
    public void SetUp() => _root = new GameObject("Tutorial Root");

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_root);

    private TutorialSequenceBehaviour NewSequence(string goName)
    {
        var go = new GameObject(goName, typeof(TutorialSequenceBehaviour));
        go.transform.SetParent(_root.transform);

        return go.GetComponent<TutorialSequenceBehaviour>();
    }

    private static TutorialStepBehaviour AddStep(TutorialSequenceBehaviour sequence, string goName)
    {
        var go = new GameObject(goName, typeof(TutorialStepBehaviour));
        go.transform.SetParent(sequence.transform);

        return go.GetComponent<TutorialStepBehaviour>();
    }

    [Test]
    public void 시퀀스ID를_비우면_게임오브젝트_이름을_쓴다()
    {
        var sut = NewSequence("intro");

        Assert.AreEqual("intro", sut.SequenceId);
        Assert.AreEqual("intro", sut.BuildSequence().Id);
    }

    [Test]
    public void 스텝ID를_비우면_게임오브젝트_이름을_쓴다()
    {
        var sequence = NewSequence("intro");
        var step = AddStep(sequence, "Step 1");

        Assert.AreEqual("Step 1", step.StepId);
        Assert.AreEqual("Step 1", step.Build().Id);
    }

    [Test]
    public void 자식_스텝을_계층_순서대로_모은다()
    {
        var sequence = NewSequence("intro");
        AddStep(sequence, "a");
        AddStep(sequence, "b");
        AddStep(sequence, "c");

        var built = sequence.BuildSequence();

        Assert.AreEqual(3, built.Steps.Count);
        Assert.AreEqual("a", built.Steps[0].Id);
        Assert.AreEqual("b", built.Steps[1].Id);
        Assert.AreEqual("c", built.Steps[2].Id);
    }

    [Test]
    public void 손자_스텝은_모으지_않는다()
    {
        var sequence = NewSequence("intro");
        var child = AddStep(sequence, "a");

        var grandChild = new GameObject("nested", typeof(TutorialStepBehaviour));
        grandChild.transform.SetParent(child.transform);

        var built = sequence.BuildSequence();

        Assert.AreEqual(1, built.Steps.Count);
        Assert.AreEqual("a", built.Steps[0].Id);
    }

    [Test]
    public void 스텝을_안_붙이면_빈_시퀀스가_만들어진다()
    {
        var sequence = NewSequence("intro");

        var built = sequence.BuildSequence();

        Assert.IsNotNull(built.Steps);
        Assert.AreEqual(0, built.Steps.Count);
        Assert.IsInstanceOf<AutoTrigger>(built.StartTrigger);
        Assert.AreEqual(ResumeMode.RestartSequence, built.ResumeMode);
    }

    [Test]
    public void 기본_트리거는_Auto다()
    {
        var sequence = NewSequence("intro");
        var step = AddStep(sequence, "a");

        var built = step.Build();

        Assert.IsInstanceOf<AutoTrigger>(built.StartTrigger);
        Assert.IsInstanceOf<AutoTrigger>(built.EndTrigger);
    }
}
