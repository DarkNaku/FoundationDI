using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using DarkNaku.FoundationDI;

public class UIInstanceFactoryTests
{
    private class V : UIView { }
    [UIPrefab("UI/Sample")]
    private class P : UIPagePresenter<V> { }

    [Test]
    public void 주어진_view로_Presenter를_생성하고_바인딩한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<V>();
        var resolver = Substitute.For<IObjectResolver>();
        var host = Substitute.For<IUIElementHost>();

        var factory = new UIInstanceFactory(resolver);
        var presenter = factory.CreatePresenter(typeof(P), view, host);

        Assert.IsInstanceOf<P>(presenter);
        Assert.AreSame(view, ((P)presenter).ViewBase);

        Object.DestroyImmediate(go);
    }
}
