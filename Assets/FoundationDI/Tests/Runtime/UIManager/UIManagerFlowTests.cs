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

    // 재Show 테스트용: OnAfterShow / OnInitializeView 호출 횟수 추적
    public class ReshowV : UIView { public int InitCount; public override void OnInitializeView() => InitCount++; }
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

    public class HideTrackV : UIView { public static int DestroyCount; protected override void OnDestroyView() => DestroyCount++; }
    [UIPrefab("UI/HideTrack")]
    public class HideTrackP : UIPagePresenter<HideTrackV>
    {
        public bool Shown; public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    private GameObject _prefab;
    private GameObject _prefab2;
    private GameObject _popupPrefab;
    private GameObject _overlayPrefab;
    private GameObject _reshowPrefab;
    private GameObject _hideTrackPrefab;

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

        _hideTrackPrefab = new GameObject("hideTrackPrefab", typeof(RectTransform));
        _hideTrackPrefab.AddComponent<HideTrackV>();
    }

    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(_prefab);
        Object.DestroyImmediate(_prefab2);
        Object.DestroyImmediate(_popupPrefab);
        Object.DestroyImmediate(_overlayPrefab);
        Object.DestroyImmediate(_reshowPrefab);
        Object.DestroyImmediate(_hideTrackPrefab);
    }

    [UnityTest]
    public IEnumerator Page_호출시_OnShow까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);

        var manager = new UIManager(settings, factory, resource);
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
        var factory = new UIInstanceFactory(resolver);

        var manager = new UIManager(settings, factory, resource);
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
        var factory = new UIInstanceFactory(resolver);

        var manager = new UIManager(settings, factory, resource);
        var p = manager.Overlay<OverlayP>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator 재Show시_새_Presenter가_생성되고_View는_풀에서_재사용된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ReshowSample").Returns(_reshowPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p = manager.Page<ReshowP>();
        await UniTask.WaitUntil(() => p.ShowCount >= 1);
        var view1 = p.ViewBase;
        Assert.AreEqual(1, ((ReshowV)view1).InitCount, "OnInitializeView 1차 1회");

        p.Hide();
        await UniTask.WaitUntil(() => !view1.gameObject.activeSelf);

        var p2 = manager.Page<ReshowP>();
        Assert.AreNotSame(p, p2, "fresh presenter: 새 인스턴스");
        await UniTask.WaitUntil(() => p2.ShowCount >= 1);

        Assert.AreEqual(1, p2.ShowCount, "fresh presenter: ShowCount는 1부터");
        Assert.AreSame(view1, p2.ViewBase, "View는 풀에서 재사용");
        Assert.AreEqual(1, ((ReshowV)p2.ViewBase).InitCount, "OnInitializeView는 물리 1회(재호출 안 됨)");

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
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

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
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

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
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var fadeGo = new GameObject("fade", typeof(RectTransform), typeof(FadeTransition));
        var fade = fadeGo.GetComponent<FadeTransition>();
        TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);

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
        Object.DestroyImmediate(fadeGo);
    });

    [UnityTest]
    public IEnumerator 같은_타입_팝업을_두번_열면_스택된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p1 = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => p1.Shown);
        var p2 = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => p2.Shown);

        Assert.AreNotSame(p1, p2, "같은 타입도 새 인스턴스");
        Assert.IsFalse(p1.ViewBase.InputEnabled, "하위 팝업 입력 차단");
        Assert.IsTrue(p2.ViewBase.InputEnabled, "상단 팝업 입력 활성");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator 같은_타입_Page_재요청은_새_인스턴스로_교체된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var a = manager.Page<P>();
        await UniTask.WaitUntil(() => a.Shown);
        var viewA = a.ViewBase;

        var a2 = manager.Page<P>();
        await UniTask.WaitUntil(() => a2.Shown);

        Assert.AreNotSame(a, a2, "같은 타입 Page 재요청 = 새 인스턴스(새로고침)");
        Assert.AreSame(viewA, a2.ViewBase, "이전 View는 풀 반환 후 재사용");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator Dispose시_활성_presenter의_OnAfterHide발화와_View파괴가_일어난다() => UniTask.ToCoroutine(async () =>
    {
        HideTrackV.DestroyCount = 0;
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/HideTrack").Returns(_hideTrackPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p = manager.Page<HideTrackP>();
        await UniTask.WaitUntil(() => p.Shown);

        manager.Dispose();
        await UniTask.Yield(); // Object.Destroy 반영

        Assert.IsTrue(p.AfterHideCalled, "Dispose 시 활성 presenter OnAfterHide 발화");
        Assert.AreEqual(1, HideTrackV.DestroyCount, "풀 View 파괴 시 OnDestroyView 호출");
    });
}
