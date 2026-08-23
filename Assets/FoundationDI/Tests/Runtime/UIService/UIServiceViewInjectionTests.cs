using System.Collections;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UIServiceViewInjectionTests
{
    public class InjectV : UIView { }

    [UIPrefab("UI/Inject")]
    public class InjectP : UIPagePresenter<InjectV>
    {
        public bool Shown;
        protected internal override void OnAfterShow() => Shown = true;
    }

    private GameObject _prefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("injectPrefab", typeof(RectTransform));
        _prefab.AddComponent<InjectV>();
    }

    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(_prefab);
    }

    // UIService 전용 풀도 컨테이너를 받아야 View 계층의 MonoBehaviour가 주입된다.
    // (Presenter는 UIInstanceFactory가 별도로 주입하므로 View 인스턴스만 대상으로 검증한다.)
    [UnityTest]
    public IEnumerator View는_풀에서_생성될때_컨테이너로_주입된다() => AwaitableTest.Run(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Inject").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        var factory = new UIInstanceFactory(resolver);

        var service = new UIService(settings, factory, resource);
        var p = service.Page<InjectP>();

        await AwaitableTest.WaitUntil(() => p.Shown);

        resolver.Received(1).Inject(Arg.Is<object>(o => o is InjectV));

        service.Dispose();
    });
}
