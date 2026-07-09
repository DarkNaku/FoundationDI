using NUnit.Framework;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIViewPoolLifecycleTests
{
    private class CountingView : UIView
    {
        public int InitCount;
        public int DestroyCount;
        public override void OnInitializeView() => InitCount++;
        protected override void OnDestroyView() => DestroyCount++;
    }

    [Test]
    public void OnCreateItem은_OnInitializeView를_1회_호출하고_비활성화한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<CountingView>();
        IPoolItem item = view;

        item.OnCreateItem();

        Assert.AreEqual(1, view.InitCount);
        Assert.IsFalse(go.activeSelf);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void OnDestroyItem은_OnDestroyView를_호출한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<CountingView>();
        IPoolItem item = view;

        item.OnDestroyItem();

        Assert.AreEqual(1, view.DestroyCount);

        Object.DestroyImmediate(go);
    }
}
