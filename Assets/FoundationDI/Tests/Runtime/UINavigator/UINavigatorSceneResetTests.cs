using System.Collections;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UINavigatorSceneResetTests
{
    public class ResetTrackV : UIView { public static int DestroyCount; protected override void OnDestroyView() => DestroyCount++; }
    [UIPrefab("UI/ResetTrack")]
    public class ResetTrackP : UIPagePresenter<ResetTrackV>
    {
        public bool Shown; public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    private GameObject _prefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("resetPrefab", typeof(RectTransform));
        _prefab.AddComponent<ResetTrackV>();
    }

    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(_prefab);
    }

    [UnityTest]
    public IEnumerator active씬_전환시_활성presenter가_teardown되고_풀View가_파괴된다() => AwaitableTest.Run(async () =>
    {
        ResetTrackV.DestroyCount = 0;
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ResetTrack").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UINavigator(settings, factory, resource);

        var p = manager.Page<ResetTrackP>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var previous = SceneManager.GetActiveScene();
        var temp = SceneManager.CreateScene("temp_reset_scene");
        SceneManager.SetActiveScene(temp);      // activeSceneChanged 발화 → 리셋
        await AwaitableTest.NextFrame();                    // Object.Destroy 반영

        Assert.IsTrue(p.AfterHideCalled, "씬 전환 시 활성 presenter OnAfterHide 발화");
        Assert.AreEqual(1, ResetTrackV.DestroyCount, "풀 View 파괴 시 OnDestroyView 호출");

        // 정리
        SceneManager.SetActiveScene(previous);
        manager.Dispose();
        await Awaitable.FromAsyncOperation(SceneManager.UnloadSceneAsync(temp));
    });

    [UnityTest]
    public IEnumerator 씬전환후_Page재요청시_새씬에서_Show까지_도달한다() => AwaitableTest.Run(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ResetTrack").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UINavigator(settings, factory, resource);

        var p1 = manager.Page<ResetTrackP>();
        await AwaitableTest.WaitUntil(() => p1.Shown);

        var previous = SceneManager.GetActiveScene();
        var temp = SceneManager.CreateScene("temp_reset_scene2");
        SceneManager.SetActiveScene(temp);
        await AwaitableTest.NextFrame();

        var p2 = manager.Page<ResetTrackP>();
        await AwaitableTest.WaitUntil(() => p2.Shown, 3f);
        Assert.IsTrue(p2.Shown, "씬 전환 후 재구성된 UINavigator에서 Page가 표시되어야 한다");
        Assert.AreNotSame(p1, p2, "씬 전환 후엔 새 presenter 인스턴스");

        SceneManager.SetActiveScene(previous);
        manager.Dispose();
        await Awaitable.FromAsyncOperation(SceneManager.UnloadSceneAsync(temp));
    });
}
