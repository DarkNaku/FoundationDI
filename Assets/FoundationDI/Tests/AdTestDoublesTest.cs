using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdTestDoublesTest
{
    [Test]
    public void 가짜_디스패처는_지정_시간이_지나야_지연작업을_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.Delay(5f, () => ran++);

        dispatcher.Advance(4.9f);
        Assert.AreEqual(0, ran, "아직 시간이 안 됐는데 실행됐다");

        dispatcher.Advance(0.2f);
        Assert.AreEqual(1, ran, "시간이 지났는데 실행되지 않았다");

        dispatcher.Advance(100f);
        Assert.AreEqual(1, ran, "한 번 실행된 작업이 다시 실행됐다");
    }

    [Test]
    public void 가짜_디스패처는_취소된_지연작업을_실행하지_않는다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        var handle = dispatcher.Delay(5f, () => ran++);
        handle.Dispose();

        dispatcher.Advance(10f);

        Assert.AreEqual(0, ran);
    }

    [Test]
    public void 가짜_디스패처는_지정_프레임수가_지나야_작업을_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.NextFrames(2, () => ran++);

        dispatcher.TickFrames(1);
        Assert.AreEqual(0, ran);

        dispatcher.TickFrames(1);
        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 가짜_디스패처는_프레임수가_0이면_즉시_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.NextFrames(0, () => ran++);

        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 가짜_디스패처는_실행중에_예약된_작업을_같은_틱에_실행하지_않는다()
    {
        // 자동 재로드가 재시도를 예약하는 상황을 재현한다.
        // 스냅샷 순회가 깨지면 여기서 무한 루프나 조기 실행이 잡힌다.
        var dispatcher = new FakeAdDispatcher();
        var outer = 0;
        var inner = 0;

        dispatcher.Delay(1f, () =>
        {
            outer++;
            dispatcher.Delay(1f, () => inner++);
        });

        dispatcher.Advance(1f);
        Assert.AreEqual(1, outer);
        Assert.AreEqual(0, inner, "중첩 예약이 같은 틱에 실행됐다");

        dispatcher.Advance(1f);
        Assert.AreEqual(1, inner);
    }
}
