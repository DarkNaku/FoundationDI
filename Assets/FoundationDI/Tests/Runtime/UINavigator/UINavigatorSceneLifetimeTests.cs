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

    [UnityTest]
    public IEnumerator Dispose_이후_Hide요청이_캔버스를_되살리지_않는다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var rootGO = p.ViewBase.transform.root.gameObject;
        nav.Dispose();
        // 파괴가 반영된 뒤에 스냅샷을 찍는다 — 한 프레임으로는 보장되지 않는다(아래
        // Dispose하면_캔버스GO가_파괴된다 테스트에서 실측한 바와 같다). before/after 사이에
        // 파괴가 끼면 개수가 줄어 재생성과 무관하게 실패한다.
        await AwaitableTest.WaitUntil(() => rootGO == null, timeoutSeconds: 5f);

        // 이름으로 GameObject.Find하지 않는 이유: 이름 일치는 태생적으로 모호하다 — 어느 출처에서
        // 왔든 "[UINavigator]"라는 이름의 캔버스면 다 걸린다. 이 테스트가 실제로 확인하고 싶은 것은
        // "dispose가 새 루트를 만들었는가"이므로, 존재 자체가 아니라 개수 델타로 재생성 여부를 판정한다.
        var before = UnityEngine.Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None).Length;

        // 게임 코드가 들고 있던 presenter로 뒤늦게 Hide를 부르는 경로.
        // 큐 → 내부 Pool/Root 접근으로 이어지면 다음 씬에 고아 캔버스가 생긴다.
        Assert.DoesNotThrow(() => p.Hide());
        await AwaitableTest.NextFrame();

        var after = UnityEngine.Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None).Length;
        Assert.AreEqual(before, after,
            "dispose 이후에는 캔버스가 다시 만들어지면 안 된다");
    });

    [UnityTest]
    public IEnumerator Dispose하면_캔버스GO가_파괴된다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var rootGO = p.ViewBase.transform.root.gameObject;
        Assert.IsTrue(rootGO != null, "사전 조건: 캔버스가 살아 있다");

        nav.Dispose();
        // Object.Destroy는 현재 Update 루프 이후로 지연된다. 실측 결과 NextFrame() 1회(심지어 3회)로도
        // 반영이 보장되지 않아(엔진 정리 시점이 우리 커스텀 프레임 펌프와 어긋난다), WaitUntil로 기다린다.
        await AwaitableTest.WaitUntil(() => rootGO == null, timeoutSeconds: 5f);

        Assert.IsTrue(rootGO == null, "Dispose는 캔버스를 파괴해야 한다");
    });

    [UnityTest]
    public IEnumerator Dispose하면_활성presenter가_OnAfterHide까지_teardown된다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        nav.Dispose();

        Assert.IsTrue(p.AfterHideCalled,
            "Dispose는 활성 presenter의 수명 콜백을 끝까지 흘려야 한다");
    });

    [UnityTest]
    public IEnumerator 캔버스가_먼저_파괴된_뒤_Dispose해도_예외가_없다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        // 씬 언로드 시 GameObject 파괴와 컨테이너 Dispose의 순서는 보장되지 않는다.
        // 캔버스가 먼저 가는 쪽을 재현한다.
        Object.DestroyImmediate(p.ViewBase.transform.root.gameObject);

        Assert.DoesNotThrow(() => nav.Dispose(),
            "이미 파괴된 캔버스에 대한 Dispose는 예외 없이 통과해야 한다");
    });
}
