using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class UIViewTransitionResolveTests
{
    private sealed class ResolveTestView : UIView { }

    [UnityTest]
    public IEnumerator 부착된_트랜지션_컴포넌트를_해석한다() => UniTask.ToCoroutine(async () =>
    {
        var go = TransitionTestHelpers.NewUINode("view", typeof(CanvasGroup), typeof(FadeTransition), typeof(ResolveTestView));
        var cg = go.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        var fade = go.GetComponent<FadeTransition>();
        TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);
        var view = go.GetComponent<ResolveTestView>();

        await view.ShowAsync(CancellationToken.None);

        Assert.AreEqual(1f, cg.alpha, 0.001f); // 컴포넌트 트랜지션이 적용됨
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    public IEnumerator 트랜지션_컴포넌트없으면_Noop으로_즉시완료() => UniTask.ToCoroutine(async () =>
    {
        var go = TransitionTestHelpers.NewUINode("view", typeof(CanvasGroup), typeof(ResolveTestView));
        var view = go.GetComponent<ResolveTestView>();

        await view.ShowAsync(CancellationToken.None); // 예외 없이 즉시 완료

        Assert.Pass();
        Object.DestroyImmediate(go);
    });
}
