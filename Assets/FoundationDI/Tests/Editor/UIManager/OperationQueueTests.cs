using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class OperationQueueTests
{
    [UnityTest]
    public IEnumerator 큐는_등록된_작업을_순서대로_직렬화한다() => UniTask.ToCoroutine(async () =>
    {
        var queue = new OperationQueue();
        var order = new List<int>();

        queue.Enqueue(async ct => { await UniTask.Yield(); order.Add(1); });
        queue.Enqueue(async ct => { order.Add(2); await UniTask.CompletedTask; });

        await UniTask.WaitUntil(() => order.Count == 2);

        Assert.AreEqual(new[] { 1, 2 }, order.ToArray());
    });

    [UnityTest]
    public IEnumerator CancelAndClear_후_대기작업은_실행되지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var queue = new OperationQueue();
        var ran = false;
        queue.Enqueue(async ct => { await UniTask.Delay(100, cancellationToken: ct); ran = true; });
        queue.CancelAndClear();
        await UniTask.Delay(200);
        Assert.IsFalse(ran);
    });
}
