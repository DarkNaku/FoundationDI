using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class FadeTransitionTests
{
    [UnityTest]
    public IEnumerator Show_완료후_알파는_1이다() => UniTask.ToCoroutine(async () =>
    {
        var go = TransitionTestHelpers.NewUINode("fade", typeof(CanvasGroup), typeof(FadeTransition));
        var cg = go.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        var fade = go.GetComponent<FadeTransition>();
        TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);

        await fade.ShowAsync((RectTransform)go.transform, CancellationToken.None);

        Assert.AreEqual(1f, cg.alpha, 0.001f);
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    public IEnumerator Hide_완료후_알파는_0이다() => UniTask.ToCoroutine(async () =>
    {
        var go = TransitionTestHelpers.NewUINode("fade", typeof(CanvasGroup), typeof(FadeTransition));
        var cg = go.GetComponent<CanvasGroup>();
        cg.alpha = 1f;
        var fade = go.GetComponent<FadeTransition>();
        TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);

        await fade.HideAsync((RectTransform)go.transform, CancellationToken.None);

        Assert.AreEqual(0f, cg.alpha, 0.001f);
        Object.DestroyImmediate(go);
    });
}
