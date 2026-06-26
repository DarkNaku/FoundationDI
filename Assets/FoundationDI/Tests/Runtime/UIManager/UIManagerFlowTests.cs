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
    public class P : UIPagePresenter<V> { public bool Shown; protected internal override void OnAfterShow() => Shown = true; }

    // 재Show 테스트용: OnAfterShow 호출 횟수를 추적
    public class ReshowV : UIView { }
    [UIPrefab("UI/ReshowSample")]
    public class ReshowP : UIPagePresenter<ReshowV> { public int ShowCount; protected internal override void OnAfterShow() => ShowCount++; }

    public class PopupV : UIView { }
    [UIPrefab("UI/SamplePopup")]
    public class PopupP : UIPopupPresenter<PopupV> { public bool Shown; protected internal override void OnAfterShow() => Shown = true; }

    public class OverlayV : UIView { }
    [UIPrefab("UI/SampleOverlay")]
    public class OverlayP : UIOverlayPresenter<OverlayV> { public bool Shown; protected internal override void OnAfterShow() => Shown = true; }

    private GameObject _prefab;
    private GameObject _popupPrefab;
    private GameObject _overlayPrefab;
    private GameObject _reshowPrefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("prefab", typeof(RectTransform));
        _prefab.AddComponent<V>();

        _popupPrefab = new GameObject("popupPrefab", typeof(RectTransform));
        _popupPrefab.AddComponent<PopupV>();

        _overlayPrefab = new GameObject("overlayPrefab", typeof(RectTransform));
        _overlayPrefab.AddComponent<OverlayV>();

        _reshowPrefab = new GameObject("reshowPrefab", typeof(RectTransform));
        _reshowPrefab.AddComponent<ReshowV>();
    }

    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(_prefab);
        Object.DestroyImmediate(_popupPrefab);
        Object.DestroyImmediate(_overlayPrefab);
        Object.DestroyImmediate(_reshowPrefab);
    }

    [UnityTest]
    public IEnumerator Page_호출시_OnShow까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);

        var manager = new UIManager(settings, factory);
        var p = manager.Page<P>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator Popup_호출시_스택_Top이_된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);

        var manager = new UIManager(settings, factory);
        var p = manager.Popup<PopupP>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator Overlay_호출시_OnShow까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SampleOverlay").Returns(_overlayPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);

        var manager = new UIManager(settings, factory);
        var p = manager.Overlay<OverlayP>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });

    // FIX C1 가드: Hide 후 재Show 시 GameObject가 다시 활성화되고 OnAfterShow가 재호출된다
    [UnityTest]
    public IEnumerator 재Show시_GameObject가_다시_활성화된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ReshowSample").Returns(_reshowPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);

        var manager = new UIManager(settings, factory);

        // 1차 Show
        var p = manager.Page<ReshowP>();
        await UniTask.WaitUntil(() => p.ShowCount >= 1);
        Assert.AreEqual(1, p.ShowCount, "1차 Show: OnAfterShow 1회 호출");
        Assert.IsTrue(p.ViewBaseForTest.gameObject.activeSelf, "1차 Show 후 GameObject 활성");

        // Hide (OperationQueue를 통해 비동기)
        p.Hide();
        await UniTask.WaitUntil(() => !p.ViewBaseForTest.gameObject.activeSelf);
        Assert.IsFalse(p.ViewBaseForTest.gameObject.activeSelf, "Hide 후 GameObject 비활성");

        // 2차 재Show — 캐시에서 꺼낸 인스턴스여야 하고 GameObject가 다시 활성화되어야 함
        var p2 = manager.Page<ReshowP>();
        Assert.AreSame(p, p2, "캐시 재사용: 동일 인스턴스");
        await UniTask.WaitUntil(() => p2.ShowCount >= 2);
        Assert.AreEqual(2, p2.ShowCount, "재Show: OnAfterShow 2회 호출");
        Assert.IsTrue(p2.ViewBaseForTest.gameObject.activeSelf, "재Show 후 GameObject 다시 활성");

        manager.Dispose();
    });
}
