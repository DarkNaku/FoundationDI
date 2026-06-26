using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class UIViewTests
{
    private class TestView : UIView { }

    [Test]
    public void InputEnabled는_CanvasGroup_interactable을_토글한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<TestView>();

        view.InputEnabled = false;
        Assert.IsFalse(go.GetComponent<CanvasGroup>().interactable);
        view.InputEnabled = true;
        Assert.IsTrue(go.GetComponent<CanvasGroup>().interactable);

        Object.DestroyImmediate(go);
    }

    [UnityTest]
    public IEnumerator Transition_오버라이드는_PlayShow와_PlayHide에_모두_적용된다() => UniTask.ToCoroutine(async () =>
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<TestView>();

        var transition = Substitute.For<IUITransition>();
        transition.PlayShow(Arg.Any<RectTransform>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
        transition.PlayHide(Arg.Any<RectTransform>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

        view.Transition = transition;
        await view.PlayShow(default);
        await view.PlayHide(default);

        transition.Received(1).PlayShow(view.RectTransform, Arg.Any<CancellationToken>());
        transition.Received(1).PlayHide(view.RectTransform, Arg.Any<CancellationToken>());

        Object.DestroyImmediate(go);
    });
}
