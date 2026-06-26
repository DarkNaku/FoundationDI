using NUnit.Framework;
using UnityEngine;
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
}
