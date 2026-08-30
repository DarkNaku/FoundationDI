using System.Collections;
using System.Text.RegularExpressions;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using DarkNaku.FoundationDI;

public class UINavigatorRootPrefabTests
{
    public class V : UIView { }

    [UIPrefab("UI/RootPrefabSample")]
    public class P : UIPagePresenter<V>
    {
        public bool Shown;
        protected internal override void OnAfterShow() => Shown = true;
    }

    // 프리팹 출처를 증명하기 위한 표식. 코드 기본값(1920x1080)과 절대 겹치지 않는 값.
    private static readonly Vector2 Marker = new(1234f, 567f);

    private GameObject _viewPrefab;
    private UIRoot _rootTemplate;

    [SetUp]
    public void SetUp()
    {
        _viewPrefab = new GameObject("view", typeof(RectTransform), typeof(CanvasGroup));
        _viewPrefab.AddComponent<V>();
        _viewPrefab.SetActive(false);

        // Instantiate의 원본은 프리팹 에셋이 아니어도 되므로, 에셋 IO 없이 씬 오브젝트로 대체한다.
        _rootTemplate = UIRoot.CreateDefault();
        _rootTemplate.name = "RootTemplate";
        _rootTemplate.GO.GetComponent<CanvasScaler>().referenceResolution = Marker;
    }

    [TearDown]
    public void TearDown()
    {
        if (_viewPrefab != null) Object.DestroyImmediate(_viewPrefab);
        if (_rootTemplate != null) Object.DestroyImmediate(_rootTemplate.GO);
    }

    private UINavigator CreateService(UINavigatorSettings settings)
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/RootPrefabSample").Returns(_viewPrefab);
        return new UINavigator(settings, new UIInstanceFactory(Substitute.For<IObjectResolver>()), resource);
    }

    [UnityTest]
    public IEnumerator Settings에_루트프리팹이_지정되면_그_프리팹을_인스턴스화한다() => AwaitableTest.Run(async () =>
    {
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        settings.RootPrefab = _rootTemplate;

        var service = CreateService(settings);
        var p = service.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var clone = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(clone, "표시된 View는 UIRoot 아래에 있어야 한다");
        Assert.AreNotSame(_rootTemplate, clone, "원본이 아니라 클론이어야 한다");
        Assert.AreEqual(Marker, clone.GO.GetComponent<CanvasScaler>().referenceResolution,
            "캔버스 설정은 프리팹에서 와야 한다(코드가 덮어쓰지 않는다)");
        Assert.AreEqual(SceneManager.GetActiveScene().handle, clone.GO.scene.handle,
            "씬 귀속은 프리팹 경로에서도 동일하게 적용되어야 한다");

        service.Dispose();
    });

    [UnityTest]
    public IEnumerator Settings에_루트프리팹이_없으면_코드_기본값으로_폴백한다() => AwaitableTest.Run(async () =>
    {
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();

        // RootPrefab 미지정은 0.3.0→0.4.0 마이그레이션의 사각지대라 컴파일 에러 없이 조용히
        // 폴백한다 — 그래서 반드시 경고를 남겨야 한다(Finding 2).
        LogAssert.Expect(LogType.Warning, new Regex(@"\[UINavigator\].*RootPrefab"));

        var service = CreateService(settings);
        var p = service.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var root = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(root);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution,
            root.GO.GetComponent<CanvasScaler>().referenceResolution);
        Assert.AreEqual(SceneManager.GetActiveScene().handle, root.GO.scene.handle);

        service.Dispose();
    });

    [UnityTest]
    public IEnumerator 루트프리팹의_레이어가_비어있으면_에러를_로그하고_UI는_계속_표시된다() => AwaitableTest.Run(async () =>
    {
        // PageLayer를 파괴해 fake-null로 만든다 — SetParent(null, false)는 예외를 던지지 않으므로
        // 검증이 없으면 UI가 조용히 씬 루트로 떨어져 화면에서 사라진다(Finding 1).
        Object.DestroyImmediate(_rootTemplate.PageLayer.gameObject);

        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        settings.RootPrefab = _rootTemplate;

        LogAssert.Expect(LogType.Error, new Regex(@"\[UINavigator\].*PageLayer"));

        var service = CreateService(settings);
        var p = service.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        Assert.IsTrue(p.Shown, "레이어가 비어 있어도 크래시 없이 표시(비록 화면 밖이라도)는 계속되어야 한다");

        service.Dispose();
    });
}
