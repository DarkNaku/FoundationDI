using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

public class UIViewTests
{
    private class TestView : UIView { }

    [Test]
    public void InputEnabled는_GraphicRaycaster를_토글한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var view = go.AddComponent<TestView>();

        view.InputEnabled = false;
        Assert.IsFalse(go.GetComponent<GraphicRaycaster>().enabled);
        view.InputEnabled = true;
        Assert.IsTrue(go.GetComponent<GraphicRaycaster>().enabled);

        Object.DestroyImmediate(go);
    }
}
