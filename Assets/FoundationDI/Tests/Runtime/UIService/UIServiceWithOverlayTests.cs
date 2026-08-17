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
}
