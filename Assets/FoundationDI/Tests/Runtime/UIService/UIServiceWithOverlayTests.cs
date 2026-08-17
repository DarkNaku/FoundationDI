using System.Collections;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UIServiceWithOverlayTests
{
    public class HostV : UIView { }

    [UIPrefab("UI/WithOvHost")]
    public class HostP : UIPagePresenter<HostV>
    {
        public bool Shown; public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    public class OvV : UIView { }

    [UIPrefab("UI/WithOvOverlay")]
    public class OvP : UIOverlayPresenter<OvV>
    {
        public bool Shown; public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    private GameObject _hostPrefab;
    private GameObject _ovPrefab;

    [SetUp]
    public void Setup()
    {
        _hostPrefab = new GameObject("hostPrefab", typeof(RectTransform));
        _hostPrefab.AddComponent<HostV>();
        _ovPrefab = new GameObject("ovPrefab", typeof(RectTransform));
        _ovPrefab.AddComponent<OvV>();
    }

    [TearDown]
    public void Teardown()
    {
        Object.DestroyImmediate(_hostPrefab);
        Object.DestroyImmediate(_ovPrefab);
    }

    [UnityTest]
    public IEnumerator WithOverlay는_Page와_오버레이를_함께_노출하고_Page_hide시_함께_숨긴다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/WithOvHost").Returns(_hostPrefab);
        resource.Load<GameObject>("UI/WithOvOverlay").Returns(_ovPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIService(settings, factory, resource);

        // 빌더 메서드는 카테고리 베이스(UIPagePresenter<HostV>)를 반환하므로
        // 구체 프레젠터(HostP) 참조를 유지하기 위해 분리 호출한다.
        var host = manager.Page<HostP>();
        host.WithOverlay<OvP>();

        await UniTask.WhenAny(UniTask.WaitUntil(() => host.Shown), UniTask.Delay(3000));
        Assert.IsTrue(host.Shown, "Page가 표시되어야 한다");

        Assert.IsNotNull(host.LinkedOverlays, "링크된 오버레이가 있어야 한다");
        Assert.AreEqual(1, host.LinkedOverlays.Count, "오버레이 1개가 함께 스폰");
        var ov = (OvP)host.LinkedOverlays[0];
        Assert.IsTrue(ov.Shown, "오버레이도 Page와 함께 표시되어야 한다");
        Assert.IsTrue(ov.ViewBase.gameObject.activeSelf, "오버레이 View가 활성이어야 한다");

        // Page를 숨기면 링크된 오버레이도 함께 숨겨진다.
        host.Hide();
        await UniTask.WhenAny(UniTask.WaitUntil(() => host.AfterHideCalled && ov.AfterHideCalled), UniTask.Delay(3000));
        Assert.IsTrue(host.AfterHideCalled, "Page가 hide되어야 한다");
        Assert.IsTrue(ov.AfterHideCalled, "Page hide 시 링크된 오버레이도 hide되어야 한다");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator persistent_오버레이는_페이지_전환시_동일_인스턴스로_연속유지된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/WithOvHost").Returns(_hostPrefab);
        resource.Load<GameObject>("UI/WithOvOverlay").Returns(_ovPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIService(settings, factory, resource);

        var a = manager.Page<HostP>();
        a.WithOverlay<OvP>(persistent: true);
        await UniTask.WhenAny(UniTask.WaitUntil(() => a.Shown), UniTask.Delay(3000));
        var ov1 = (OvP)a.LinkedOverlays[0];
        Assert.IsTrue(ov1.Shown);

        var b = manager.Page<HostP>();
        b.WithOverlay<OvP>(persistent: true);
        await UniTask.WhenAny(UniTask.WaitUntil(() => b.Shown), UniTask.Delay(3000));

        Assert.AreEqual(1, b.LinkedOverlays.Count);
        Assert.AreSame(ov1, (OvP)b.LinkedOverlays[0], "persistent 오버레이는 전환 시 동일 인스턴스로 이전되어야 한다");
        Assert.IsFalse(ov1.AfterHideCalled, "연속 유지 오버레이는 전환 중 hide되지 않아야 한다");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator 기본_오버레이는_페이지_전환시_새_인스턴스로_재생성된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/WithOvHost").Returns(_hostPrefab);
        resource.Load<GameObject>("UI/WithOvOverlay").Returns(_ovPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIService(settings, factory, resource);

        var a = manager.Page<HostP>();
        a.WithOverlay<OvP>(); // 기본(non-persistent)
        await UniTask.WhenAny(UniTask.WaitUntil(() => a.Shown), UniTask.Delay(3000));
        var ov1 = (OvP)a.LinkedOverlays[0];

        var b = manager.Page<HostP>();
        b.WithOverlay<OvP>();
        await UniTask.WhenAny(UniTask.WaitUntil(() => b.Shown), UniTask.Delay(3000));

        Assert.AreNotSame(ov1, (OvP)b.LinkedOverlays[0], "기본은 호스트별 새 인스턴스로 재생성되어야 한다");
        Assert.IsTrue(ov1.AfterHideCalled, "기본은 전환 시 이전 오버레이가 hide되어야 한다");

        manager.Dispose();
    });
}
