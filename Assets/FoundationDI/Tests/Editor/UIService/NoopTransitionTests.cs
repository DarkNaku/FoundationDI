using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class NoopTransitionTests
{
    [UnityTest]
    public IEnumerator Noop은_즉시_완료된다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("t", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        IUITransition noop = new NoopTransition();

        await noop.ShowAsync(rt, CancellationToken.None);
        await noop.HideAsync(rt, CancellationToken.None);

        Assert.Pass();
        Object.DestroyImmediate(go);
    });
}
