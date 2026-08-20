using DarkNaku.FoundationDI;
using NUnit.Framework;

public class UnityAdDispatcherTest
{
    // createRunner: false 로 MonoBehaviour 없이 순수 큐 로직만 검증한다.
    private static UnityAdDispatcher NewDispatcher() => new(createRunner: false);

    [Test]
    public void Post한_작업은_다음_펌프에서_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Post(() => ran++);
        Assert.AreEqual(0, ran, "Post가 즉시 실행됐다 — 마샬링 의미가 없다");

        sut.Pump(0.016f);
        Assert.AreEqual(1, ran);

        sut.Pump(0.016f);
        Assert.AreEqual(1, ran, "한 번 실행된 작업이 다시 실행됐다");
    }
}
