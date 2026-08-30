// using System; 을 넣지 않는다 — using UnityEngine; 과 함께 쓰면 Object 가 모호해져 컴파일이 깨진다.
using System.Collections;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UINavigatorSceneLifetimeTests
{
    public class V : UIView { }

    [UIPrefab("UI/SceneLifetime")]
    public class P : UIPagePresenter<V>
    {
        public bool Shown;
        public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    private GameObject _viewPrefab;

    [SetUp]
    public void SetUp()
    {
        // Instantiate 원본은 프리팹 에셋이 아니어도 되므로 에셋 IO 없이 씬 오브젝트로 대체한다.
        _viewPrefab = new GameObject("view", typeof(RectTransform), typeof(CanvasGroup));
        _viewPrefab.AddComponent<V>();
        _viewPrefab.SetActive(false);
    }

    [TearDown]
    public void TearDown()
    {
        if (_viewPrefab != null) Object.DestroyImmediate(_viewPrefab);
    }

    // RootPrefab 미지정 → UIRoot.CreateDefault() 폴백. 경고 로그가 남지만 경고는 테스트를 깨뜨리지 않는다.
    private UINavigator CreateNavigator()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SceneLifetime").Returns(_viewPrefab);
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        return new UINavigator(settings, new UIInstanceFactory(Substitute.For<IObjectResolver>()), resource);
    }

    [UnityTest]
    public IEnumerator 캔버스는_상주씬이_아니라_활성씬에_속한다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var root = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(root, "표시된 View는 UIRoot 아래에 있어야 한다");
        Assert.AreNotEqual("DontDestroyOnLoad", root.GO.scene.name,
            "캔버스가 상주하면 씬과 함께 파괴되지 않는다");
        Assert.AreEqual(SceneManager.GetActiveScene().handle, root.GO.scene.handle,
            "캔버스는 자신을 만든 시점의 활성 씬에 속해야 한다");

        nav.Dispose();
    });

    [UnityTest]
    public IEnumerator 활성씬이_바뀌어도_표시중인_UI를_스스로_리셋하지_않는다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var previous = SceneManager.GetActiveScene();
        var temp = SceneManager.CreateScene("uinavigator_scene_switch");
        SceneManager.SetActiveScene(temp);
        await AwaitableTest.NextFrame();

        // 정리 경로는 Dispose 하나뿐이다. 씬 이벤트는 더 이상 teardown을 촉발하지 않는다.
        Assert.IsFalse(p.AfterHideCalled, "씬 전환이 presenter를 teardown하면 안 된다");
        Assert.IsTrue(p.ViewBase != null, "표시 중인 View가 파괴되면 안 된다");
        Assert.IsTrue(p.ViewBase.gameObject.activeSelf, "표시 중인 View가 비활성화되면 안 된다");

        SceneManager.SetActiveScene(previous);
        nav.Dispose();
        await Awaitable.FromAsyncOperation(SceneManager.UnloadSceneAsync(temp));
    });
}
