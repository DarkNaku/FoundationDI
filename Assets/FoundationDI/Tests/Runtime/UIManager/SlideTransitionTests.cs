using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class SlideTransitionTests
{
    private static SlideTransition NewSlide(out GameObject root, out RectTransform content, out CanvasGroup bg)
    {
        root = TransitionTestHelpers.NewUINode("slide", typeof(SlideTransition));
        var contentGo = TransitionTestHelpers.NewUINode("content");
        content = (RectTransform)contentGo.transform;
        content.SetParent(root.transform, false);
        content.sizeDelta = new Vector2(200, 200);
        content.anchoredPosition = Vector2.zero;

        var bgGo = TransitionTestHelpers.NewUINode("bg", typeof(CanvasGroup));
        bg = bgGo.GetComponent<CanvasGroup>();
        bgGo.transform.SetParent(root.transform, false);

        var slide = root.GetComponent<SlideTransition>();
        TransitionTestHelpers.SetPrivate(slide, "_duration", 0.05f);
        return slide;
    }

    [UnityTest]
    public IEnumerator Show_완료후_컨텐츠는_home으로_배경알파는_1이다() => UniTask.ToCoroutine(async () =>
    {
        var slide = NewSlide(out var root, out var content, out var bg);
        TransitionTestHelpers.SetPrivate(slide, "_content", content);
        TransitionTestHelpers.SetPrivate(slide, "_background", bg);

        await slide.ShowAsync((RectTransform)root.transform, CancellationToken.None);

        Assert.AreEqual(Vector2.zero, content.anchoredPosition);
        Assert.AreEqual(1f, bg.alpha, 0.001f);
        Object.DestroyImmediate(root);
    });

    [UnityTest]
    public IEnumerator Hide_완료후_컨텐츠는_home으로_복원된다() => UniTask.ToCoroutine(async () =>
    {
        var slide = NewSlide(out var root, out var content, out var bg);
        TransitionTestHelpers.SetPrivate(slide, "_content", content);
        TransitionTestHelpers.SetPrivate(slide, "_background", bg);

        await slide.HideAsync((RectTransform)root.transform, CancellationToken.None);

        Assert.AreEqual(Vector2.zero, content.anchoredPosition);
        Object.DestroyImmediate(root);
    });

    [UnityTest]
    public IEnumerator 배경이_null이면_페이드생략하고_컨텐츠만_이동() => UniTask.ToCoroutine(async () =>
    {
        var slide = NewSlide(out var root, out var content, out var bg);
        TransitionTestHelpers.SetPrivate(slide, "_content", content);
        // _background 미주입

        await slide.ShowAsync((RectTransform)root.transform, CancellationToken.None);

        Assert.AreEqual(Vector2.zero, content.anchoredPosition);
        Object.DestroyImmediate(root);
    });

    [UnityTest]
    public IEnumerator 컨텐츠가_null이면_루트가_이동대상() => UniTask.ToCoroutine(async () =>
    {
        var slide = NewSlide(out var root, out var content, out var bg);
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(100, 100);
        rootRt.anchoredPosition = Vector2.zero;
        // _content, _background 미주입

        await slide.ShowAsync(rootRt, CancellationToken.None);

        Assert.AreEqual(Vector2.zero, rootRt.anchoredPosition);
        Object.DestroyImmediate(root);
    });
}
