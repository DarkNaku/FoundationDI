using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class NoopTransitionTests
{
    [UnityTest]
    public IEnumerator Noop은_즉시_완료된다() => UniTask.ToCoroutine(async () =>
    {
        var go = new GameObject("t", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        IUITransition noop = new NoopTransition();

        await noop.PlayShow(rt, CancellationToken.None);
        await noop.PlayHide(rt, CancellationToken.None);

        Assert.Pass();
        Object.DestroyImmediate(go);
    });
}
