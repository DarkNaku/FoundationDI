using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class TransitionAssetTests
{
    [UnityTest]
    public IEnumerator Fade_ShowAsync_종료시_alpha는_1이다() => UniTask.ToCoroutine(async () =>
    {
        var go = new GameObject("t", typeof(RectTransform), typeof(CanvasGroup));
        var rt = go.GetComponent<RectTransform>();
        var fade = ScriptableObject.CreateInstance<FadeTransitionAsset>();

        await fade.ShowAsync(rt, CancellationToken.None);

        Assert.AreEqual(1f, go.GetComponent<CanvasGroup>().alpha, 0.001f);
        Object.DestroyImmediate(go);
    });
}
