using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class OperationQueueTests
{
    [UnityTest]
    public IEnumerator 큐는_등록된_작업을_순서대로_직렬화한다() => AwaitableTest.Run(async () =>
    {
        var queue = new OperationQueue();
        var order = new List<int>();

        queue.Enqueue(async ct => { await AwaitableTest.NextFrame(); order.Add(1); });
        queue.Enqueue(async ct => { order.Add(2); await AwaitableTest.Completed(); });

        await AwaitableTest.WaitUntil(() => order.Count == 2);

        Assert.AreEqual(new[] { 1, 2 }, order.ToArray());
    });

    [UnityTest]
    public IEnumerator CancelAndClear_후_대기작업은_실행되지_않는다() => AwaitableTest.Run(async () =>
    {
        var queue = new OperationQueue();
        var ran = false;
        queue.Enqueue(async ct => { await AwaitableTest.Delay(100, cancellationToken: ct); ran = true; });
        queue.CancelAndClear();
        await AwaitableTest.Delay(200);
        Assert.IsFalse(ran);
    });
}
