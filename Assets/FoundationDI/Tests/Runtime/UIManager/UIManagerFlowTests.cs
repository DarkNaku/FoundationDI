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

    // Page 교체(A→B) 재현용 둘째 Page 타입
    public class V2 : UIView { }
    [UIPrefab("UI/Sample2")]
    public class P2 : UIPagePresenter<V2> { public bool Shown; protected internal override void OnAfterShow() => Shown = true; }

    private GameObject _prefab;
    private GameObject _prefab2;
    private GameObject _popupPrefab;
    private GameObject _overlayPrefab;
    private GameObject _reshowPrefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("prefab", typeof(RectTransform));
        _prefab.AddComponent<V>();

        _prefab2 = new GameObject("prefab2", typeof(RectTransform));
        _prefab2.AddComponent<V2>();

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
        Object.DestroyImmediate(_prefab2);
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
        Assert.IsTrue(p.ViewBase.gameObject.activeSelf, "1차 Show 후 GameObject 활성");

        // Hide (OperationQueue를 통해 비동기)
        p.Hide();
        await UniTask.WaitUntil(() => !p.ViewBase.gameObject.activeSelf);
        Assert.IsFalse(p.ViewBase.gameObject.activeSelf, "Hide 후 GameObject 비활성");

        // 2차 재Show — 캐시에서 꺼낸 인스턴스여야 하고 GameObject가 다시 활성화되어야 함
        var p2 = manager.Page<ReshowP>();
        Assert.AreSame(p, p2, "캐시 재사용: 동일 인스턴스");
        await UniTask.WaitUntil(() => p2.ShowCount >= 2);
        Assert.AreEqual(2, p2.ShowCount, "재Show: OnAfterShow 2회 호출");
        Assert.IsTrue(p2.ViewBase.gameObject.activeSelf, "재Show 후 GameObject 다시 활성");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator 팝업_표시시_하위_Page_입력이_차단되고_팝업은_활성이다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);
        var manager = new UIManager(settings, factory);

        var page = manager.Page<P>();
        await UniTask.WaitUntil(() => page.Shown);
        Assert.IsTrue(page.ViewBase.InputEnabled, "팝업 없을 때 Page 입력 활성");

        var popup = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => popup.Shown);
        Assert.IsFalse(page.ViewBase.InputEnabled, "팝업 표시 시 Page 입력 차단");
        Assert.IsTrue(popup.ViewBase.InputEnabled, "최상단 팝업은 입력 활성");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator 팝업_표시중_AboveOverlay_입력은_유지된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SampleOverlay").Returns(_overlayPrefab);
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);
        var manager = new UIManager(settings, factory);

        var overlay = manager.Overlay<OverlayP>();
        await UniTask.WaitUntil(() => overlay.Shown);

        var popup = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => popup.Shown);

        Assert.IsTrue(overlay.ViewBase.InputEnabled, "AboveOverlay는 모달 팝업 중에도 입력 유지");

        manager.Dispose();
    });

    // 재현: Fade 트랜지션을 사용한 Page 교체(A→B)에서 새 Page가 표시되는가
    [UnityTest]
    public IEnumerator Fade트랜지션_Page교체시_새Page로_전환된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        resource.Load<GameObject>("UI/Sample2").Returns(_prefab2);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);
        var manager = new UIManager(settings, factory);

        var fade = ScriptableObject.CreateInstance<FadeTransitionAsset>();

        var a = manager.Page<P>();
        a.WithTransition(fade);
        await UniTask.WaitUntil(() => a.Shown);
        Assert.IsTrue(a.Shown, "Page A 표시");

        var b = manager.Page<P2>();
        b.WithTransition(fade);
        // hang 가드: 3초 내 B가 표시되지 않으면 hang으로 간주
        await UniTask.WhenAny(UniTask.WaitUntil(() => b.Shown), UniTask.Delay(3000));
        Assert.IsTrue(b.Shown, "Page 교체 후 B가 3초 내 표시되어야 함(미표시 시 HideAsync hang)");

        manager.Dispose();
    });
}
