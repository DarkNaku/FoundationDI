using System.Collections;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Reflex.Core;
using DarkNaku.FoundationDI;

public class UINavigatorViewInjectionTests
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

    // UINavigator 전용 풀도 리졸버를 받아야 View 계층의 MonoBehaviour가 주입된다.
    // View는 PoolManager가 InjectGameObject로 계층째 주입하고,
    // Presenter는 UIInstanceFactory가 Inject로 따로 주입한다 - 여기서는 View 경로만 본다.
    [UnityTest]
    public IEnumerator View는_풀에서_생성될때_컨테이너로_주입된다() => AwaitableTest.Run(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Inject").Returns(_prefab);
        var resolver = Substitute.For<IServiceResolver>();
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        var factory = new UIInstanceFactory(resolver);

        var service = new UINavigator(settings, factory, resource);
        var p = service.Page<InjectP>();

        await AwaitableTest.WaitUntil(() => p.Shown);

        resolver.Received(1).InjectGameObject(
            Arg.Is<GameObject>(go => go.GetComponent<InjectV>() != null));

        service.Dispose();
    });
}
