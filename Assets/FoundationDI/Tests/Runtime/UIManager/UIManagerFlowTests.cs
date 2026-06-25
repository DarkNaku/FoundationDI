using System.Collections;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UIManagerFlowTests
{
    public class V : UIView { }
    [UIPrefab("UI/Sample")]
    public class P : UIPagePresenter<V> { public bool Shown; protected internal override void OnShow() => Shown = true; }

    public class PopupV : UIView { }
    [UIPrefab("UI/SamplePopup")]
    public class PopupP : UIPopupPresenter<PopupV> { public bool Shown; protected internal override void OnShow() => Shown = true; }

    public class OverlayV : UIView { }
    [UIPrefab("UI/SampleOverlay")]
    public class OverlayP : UIOverlayPresenter<OverlayV> { public bool Shown; protected internal override void OnShow() => Shown = true; }

    private GameObject _prefab;
    private GameObject _popupPrefab;
    private GameObject _overlayPrefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("prefab", typeof(RectTransform));
        _prefab.AddComponent<V>();

        _popupPrefab = new GameObject("popupPrefab", typeof(RectTransform));
        _popupPrefab.AddComponent<PopupV>();

        _overlayPrefab = new GameObject("overlayPrefab", typeof(RectTransform));
        _overlayPrefab.AddComponent<OverlayV>();
    }

    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(_prefab);
        Object.DestroyImmediate(_popupPrefab);
        Object.DestroyImmediate(_overlayPrefab);
    }

    [UnityTest]
    public IEnumerator Page_호출시_OnShow까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, loader);

        var manager = new UIManager(settings, factory);
        var p = manager.Page<P>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator Popup_호출시_스택_Top이_된다() => UniTask.ToCoroutine(async () =>
    {
        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, loader);

        var manager = new UIManager(settings, factory);
        var p = manager.Popup<PopupP>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator Overlay_호출시_OnShow까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("UI/SampleOverlay").Returns(_overlayPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, loader);

        var manager = new UIManager(settings, factory);
        var p = manager.Overlay<OverlayP>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });
}
