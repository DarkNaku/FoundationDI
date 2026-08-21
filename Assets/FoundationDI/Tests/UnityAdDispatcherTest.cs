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

    [Test]
    public void 지연작업은_누적_deltaTime이_지연시간에_도달하면_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Delay(0.1f, () => ran++);

        sut.Pump(0.04f);
        sut.Pump(0.04f);
        Assert.AreEqual(0, ran, "0.08초에 실행됐다");

        sut.Pump(0.04f);
        Assert.AreEqual(1, ran, "0.12초인데 실행되지 않았다");
    }

    [Test]
    public void 취소된_지연작업은_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Delay(0.1f, () => ran++).Dispose();

        sut.Pump(1f);

        Assert.AreEqual(0, ran);
    }

    [Test]
    public void 프레임_기반_작업은_지정_펌프_횟수_후_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.NextFrames(2, () => ran++);

        sut.Pump(0.016f);
        Assert.AreEqual(0, ran);

        sut.Pump(0.016f);
        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 실행중에_예약된_작업은_같은_펌프에서_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var inner = 0;

        sut.Delay(0.01f, () => sut.Delay(0.01f, () => inner++));

        sut.Pump(1f);
        Assert.AreEqual(0, inner, "중첩 예약이 같은 펌프에서 실행됐다");

        sut.Pump(1f);
        Assert.AreEqual(1, inner);
    }

    // Post 드레인 도중(네이티브 스레드 콜백 마샬링 경로) 예약된 프레임 작업이
    // 같은 펌프에서 곧바로 틱되면 안 된다 — NextFrames(1)이 0프레임으로 축소되는
    // 회귀를 잡는다. FakeAdDispatcher.Post는 동기 실행이라 이 경로를 재현하지
    // 못하므로 정책 계층 테스트로는 커버되지 않는다.
    [Test]
    public void Post_콜백에서_예약된_프레임작업은_같은_펌프에서_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var inner = 0;

        sut.Post(() => sut.NextFrames(1, () => inner++));

        sut.Pump(0.016f);
        Assert.AreEqual(0, inner, "Post로 예약한 NextFrames(1)이 같은 펌프에서 실행됐다");

        sut.Pump(0.016f);
        Assert.AreEqual(1, inner);
    }

    [Test]
    public void 한_작업이_예외를_던져도_나머지_작업은_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Post(() => throw new System.InvalidOperationException("boom"));
        sut.Post(() => ran++);

        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception,
            new System.Text.RegularExpressions.Regex("InvalidOperationException"));
        sut.Pump(0.016f);

        Assert.AreEqual(1, ran, "앞 작업의 예외가 뒤 작업을 막았다");
    }

    [Test]
    public void Dispose하면_예약된_작업이_더_이상_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Delay(0.1f, () => ran++);
        sut.Post(() => ran++);

        sut.Dispose();
        sut.Pump(1f);

        Assert.AreEqual(0, ran);
    }

    [Test]
    public void 프레임0으로_예약한_작업은_펌프_없이_즉시_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.NextFrames(0, () => ran++);

        Assert.AreEqual(1, ran, "count<=0인데 즉시 실행되지 않았다");
    }

    [Test]
    public void Dispose_이후_프레임0_예약은_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Dispose();
        sut.NextFrames(0, () => ran++);

        Assert.AreEqual(0, ran, "Dispose 이후에도 즉시 실행 경로가 살아있다");
    }

    [Test]
    public void 프레임0_콜백이_예외를_던지면_전파되지_않고_로그된다()
    {
        var sut = NewDispatcher();

        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception,
            new System.Text.RegularExpressions.Regex("InvalidOperationException"));

        Assert.DoesNotThrow(() =>
            sut.NextFrames(0, () => throw new System.InvalidOperationException("boom")));
    }
}
