using System.Collections;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using DarkNaku.FoundationDI;

public class UIServiceRootPrefabTests
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

    private UIService CreateService(UIServiceSettings settings)
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/RootPrefabSample").Returns(_viewPrefab);
        return new UIService(settings, new UIInstanceFactory(Substitute.For<IObjectResolver>()), resource);
    }

    [UnityTest]
    public IEnumerator Settings에_루트프리팹이_지정되면_그_프리팹을_인스턴스화한다() => UniTask.ToCoroutine(async () =>
    {
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        settings.RootPrefab = _rootTemplate;

        var service = CreateService(settings);
        var p = service.Page<P>();
        await UniTask.WaitUntil(() => p.Shown);

        var clone = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(clone, "표시된 View는 UIRoot 아래에 있어야 한다");
        Assert.AreNotSame(_rootTemplate, clone, "원본이 아니라 클론이어야 한다");
        Assert.AreEqual(Marker, clone.GO.GetComponent<CanvasScaler>().referenceResolution,
            "캔버스 설정은 프리팹에서 와야 한다(코드가 덮어쓰지 않는다)");
        Assert.AreEqual("DontDestroyOnLoad", clone.GO.scene.name,
            "상주화는 프리팹 경로에서도 적용되어야 한다");

        service.Dispose();
    });

    [UnityTest]
    public IEnumerator Settings에_루트프리팹이_없으면_코드_기본값으로_폴백한다() => UniTask.ToCoroutine(async () =>
    {
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();

        var service = CreateService(settings);
        var p = service.Page<P>();
        await UniTask.WaitUntil(() => p.Shown);

        var root = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(root);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution,
            root.GO.GetComponent<CanvasScaler>().referenceResolution);
        Assert.AreEqual("DontDestroyOnLoad", root.GO.scene.name);

        service.Dispose();
    });
}
