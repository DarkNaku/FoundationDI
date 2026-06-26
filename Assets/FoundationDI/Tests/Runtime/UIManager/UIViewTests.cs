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
    public IEnumerator Transition_오버라이드는_ShowAsync와_HideAsync에_모두_적용된다() => UniTask.ToCoroutine(async () =>
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<TestView>();

        var transition = Substitute.For<IUITransition>();
        transition.ShowAsync(Arg.Any<RectTransform>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
        transition.HideAsync(Arg.Any<RectTransform>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

        view.Transition = transition;
        await view.ShowAsync(default);
        await view.HideAsync(default);

        transition.Received(1).ShowAsync(view.RectTransform, Arg.Any<CancellationToken>());
        transition.Received(1).HideAsync(view.RectTransform, Arg.Any<CancellationToken>());

        Object.DestroyImmediate(go);
    });
}
