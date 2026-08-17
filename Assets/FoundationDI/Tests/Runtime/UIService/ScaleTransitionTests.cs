using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class ScaleTransitionTests
{
    [UnityTest]
    public IEnumerator Show_완료후_컨텐츠스케일1_배경알파1() => UniTask.ToCoroutine(async () =>
    {
        var root = TransitionTestHelpers.NewUINode("scale", typeof(ScaleTransition));
        var contentGo = TransitionTestHelpers.NewUINode("content");
        var content = (RectTransform)contentGo.transform;
        content.SetParent(root.transform, false);
        var bgGo = TransitionTestHelpers.NewUINode("bg", typeof(Image));
        var bg = bgGo.GetComponent<Image>();
        bgGo.transform.SetParent(root.transform, false);

        var scale = root.GetComponent<ScaleTransition>();
        TransitionTestHelpers.SetPrivate(scale, "_duration", 0.05f);
        TransitionTestHelpers.SetPrivate(scale, "_content", content);
        TransitionTestHelpers.SetPrivate(scale, "_background", bg);

        await scale.ShowAsync((RectTransform)root.transform, CancellationToken.None);

        Assert.AreEqual(Vector3.one, content.localScale);
        Assert.AreEqual(1f, bg.color.a, 0.001f);
        Object.DestroyImmediate(root);
    });

    [UnityTest]
    public IEnumerator Show_완료후_배경은_디자인알파까지_페이드된다() => UniTask.ToCoroutine(async () =>
    {
        var root = TransitionTestHelpers.NewUINode("scale", typeof(ScaleTransition));
        var contentGo = TransitionTestHelpers.NewUINode("content");
        var content = (RectTransform)contentGo.transform;
        content.SetParent(root.transform, false);
        var bgGo = TransitionTestHelpers.NewUINode("bg", typeof(Image));
        var bg = bgGo.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        bgGo.transform.SetParent(root.transform, false);

        var scale = root.GetComponent<ScaleTransition>();
        TransitionTestHelpers.SetPrivate(scale, "_duration", 0.05f);
        TransitionTestHelpers.SetPrivate(scale, "_content", content);
        TransitionTestHelpers.SetPrivate(scale, "_background", bg);

        await scale.ShowAsync((RectTransform)root.transform, CancellationToken.None);

        Assert.AreEqual(0.6f, bg.color.a, 0.001f);
        Object.DestroyImmediate(root);
    });

    [UnityTest]
    public IEnumerator 배경이_null이면_스케일만_수행() => UniTask.ToCoroutine(async () =>
    {
        var root = TransitionTestHelpers.NewUINode("scale", typeof(ScaleTransition));
        var contentGo = TransitionTestHelpers.NewUINode("content");
        var content = (RectTransform)contentGo.transform;
        content.SetParent(root.transform, false);

        var scale = root.GetComponent<ScaleTransition>();
        TransitionTestHelpers.SetPrivate(scale, "_duration", 0.05f);
        TransitionTestHelpers.SetPrivate(scale, "_content", content);
        // _background 미주입

        await scale.ShowAsync((RectTransform)root.transform, CancellationToken.None);

        Assert.AreEqual(Vector3.one, content.localScale);
        Object.DestroyImmediate(root);
    });
}
