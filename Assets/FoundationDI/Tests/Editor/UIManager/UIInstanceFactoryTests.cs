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
    public void 로더가_제공한_prefab으로_Presenter를_생성하고_바인딩한다()
    {
        var prefab = new GameObject("prefab", typeof(RectTransform));
        prefab.AddComponent<V>();

        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("UI/Sample").Returns(prefab);

        var resolver = Substitute.For<IObjectResolver>();
        var host = Substitute.For<IUIElementHost>();

        var factory = new UIInstanceFactory(resolver, loader);
        var presenter = factory.Create(typeof(P), host);

        Assert.IsInstanceOf<P>(presenter);
        Assert.IsNotNull(((P)presenter).ViewBaseForTest);   // Bind 확인용 internal 노출

        Object.DestroyImmediate(prefab);
    }
}
