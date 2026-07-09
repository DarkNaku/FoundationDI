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

    [Test]
    public void Release는_delay를_무시하고_즉시_반환한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<CountingView>();

        var released = false;
        var pool = new UnityEngine.Pool.ObjectPool<IPoolItem>(
            createFunc: () => view,
            actionOnRelease: _ => released = true);
        var pd = new PoolData(pool);

        pd.Get();                        // 'view'를 꺼내며 view.PD = pd 설정
        ((IPoolItem)view).Release(5f);   // delay를 줘도 즉시 PD.Release 경로

        Assert.IsTrue(released, "delay를 전달해도 actionOnRelease가 동기 호출됨(즉시 반환)");

        Object.DestroyImmediate(go);
    }
}
