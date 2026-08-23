using DarkNaku.FoundationDI;
using NUnit.Framework;

public class TutorialProgressStorageTest
{
    private const string SaveKey = "unittest";

    private PlayerPrefsTutorialProgressStorage NewStorage() =>
        new PlayerPrefsTutorialProgressStorage(SaveKey);

    [SetUp]
    public void SetUp() => NewStorage().Clear();

    [TearDown]
    public void TearDown() => NewStorage().Clear();

    [Test]
    public void 저장한적_없는_시퀀스는_NotStarted다()
    {
        var sut = NewStorage();

        Assert.AreEqual(TutorialState.NotStarted, sut.GetState("intro"));
        Assert.AreEqual(0, sut.GetStepIndex("intro"));
    }

    [Test]
    public void 상태를_저장하면_새_인스턴스에서도_읽힌다()
    {
        NewStorage().SetState("intro", TutorialState.Completed);

        Assert.AreEqual(TutorialState.Completed, NewStorage().GetState("intro"));
    }

    [Test]
    public void 시퀀스마다_상태가_독립적이다()
    {
        var sut = NewStorage();

        sut.SetState("intro", TutorialState.Completed);
        sut.SetState("level3", TutorialState.Running);

        Assert.AreEqual(TutorialState.Completed, sut.GetState("intro"));
        Assert.AreEqual(TutorialState.Running, sut.GetState("level3"));
        Assert.AreEqual(TutorialState.NotStarted, sut.GetState("level5"));
    }

    [Test]
    public void 스텝인덱스를_저장하면_새_인스턴스에서도_읽힌다()
    {
        NewStorage().SetStepIndex("intro", 3);

        Assert.AreEqual(3, NewStorage().GetStepIndex("intro"));
    }

    [Test]
    public void AllSkipped는_기본이_거짓이고_저장하면_유지된다()
    {
        Assert.IsFalse(NewStorage().AllSkipped);

        NewStorage().AllSkipped = true;

        Assert.IsTrue(NewStorage().AllSkipped);
    }

    [Test]
    public void Clear는_상태와_스텝인덱스와_AllSkipped를_모두_지운다()
    {
        var sut = NewStorage();
        sut.SetState("intro", TutorialState.Completed);
        sut.SetStepIndex("intro", 2);
        sut.AllSkipped = true;

        sut.Clear();

        Assert.AreEqual(TutorialState.NotStarted, sut.GetState("intro"));
        Assert.AreEqual(0, sut.GetStepIndex("intro"));
        Assert.IsFalse(sut.AllSkipped);
    }

    [Test]
    public void 저장키가_다르면_진행도가_섞이지_않는다()
    {
        var a = new PlayerPrefsTutorialProgressStorage("unittest");
        var b = new PlayerPrefsTutorialProgressStorage("unittest_other");

        try
        {
            a.SetState("intro", TutorialState.Completed);

            Assert.AreEqual(TutorialState.NotStarted, b.GetState("intro"));
        }
        finally
        {
            b.Clear();
        }
    }
}
