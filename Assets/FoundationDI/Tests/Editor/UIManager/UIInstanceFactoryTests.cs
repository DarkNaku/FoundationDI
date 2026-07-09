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
    public void Host만_바인딩하고_View는_나중에_BindView로_설정된다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var host = Substitute.For<IUIElementHost>();
        var factory = new UIInstanceFactory(resolver);

        var presenter = factory.CreatePresenter(typeof(P), host);

        Assert.IsInstanceOf<P>(presenter);
        resolver.Received(1).Inject(presenter);
        Assert.IsNull(((P)presenter).ViewBase, "생성 직후에는 View 미바인딩");

        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<V>();
        presenter.BindView(view);
        Assert.AreSame(view, ((P)presenter).ViewBase);

        Object.DestroyImmediate(go);
    }
}
