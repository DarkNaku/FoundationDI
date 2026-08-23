using DarkNaku.FoundationDI;
using NUnit.Framework;
using VContainer;

public class TutorialManagerRegistrationTest
{
    [Test]
    public void 등록하면_ITutorialManager를_해결할_수_있다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager("unittest_di");

        using var container = builder.Build();
        var sut = container.Resolve<ITutorialManager>();

        Assert.IsNotNull(sut);
        Assert.IsInstanceOf<TutorialManager>(sut);
    }

    [Test]
    public void 등록하면_타깃_레지스트리도_함께_해결된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager("unittest_di");

        using var container = builder.Build();

        Assert.IsInstanceOf<TutorialTargetRegistry>(container.Resolve<ITutorialTargetRegistry>());
        Assert.IsInstanceOf<TutorialClock>(container.Resolve<ITutorialClock>());
        Assert.IsInstanceOf<PlayerPrefsTutorialProgressStorage>(
            container.Resolve<ITutorialProgressStorage>());
    }

    [Test]
    public void ITutorialManager는_싱글톤이다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager("unittest_di");

        using var container = builder.Build();

        Assert.AreSame(container.Resolve<ITutorialManager>(),
                       container.Resolve<ITutorialManager>());
    }

    [Test]
    public void 저장소를_직접_주입하면_그것이_쓰인다()
    {
        var storage = new FakeProgressStorage();
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager(storage);

        using var container = builder.Build();

        Assert.AreSame(storage, container.Resolve<ITutorialProgressStorage>());

        storage.SetState("intro", TutorialState.Completed);

        Assert.IsTrue(container.Resolve<ITutorialManager>().IsCompleted("intro"));
    }
}
