using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

public class PoolManagerTest
{
    [Test]
    public void Get은_프리팹_로드를_ResourceService에_위임한다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var sut = new PoolManager(resource, null);

        sut.Get("enemy");

        resource.Received(1).Load<GameObject>("enemy");

        sut.Dispose();
        Object.DestroyImmediate(prefab);
    }

    [Test]
    public void 같은_키_재요청시_ResourceService_로드를_다시_호출하지_않는다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var sut = new PoolManager(resource, null);

        sut.Get("enemy");
        sut.Get("enemy");

        resource.Received(1).Load<GameObject>("enemy");

        sut.Dispose();
        Object.DestroyImmediate(prefab);
    }

    [Test]
    public void 부모_Transform이_주어지면_풀_루트를_그_아래에_둔다()
    {
        var parent = new GameObject("scope");
        var resource = Substitute.For<IResourceService>();
        var sut = new PoolManager(resource, null, parent.transform);

        var root = parent.transform.Find("[PoolManager]");

        Assert.IsNotNull(root);

        sut.Dispose();
        Object.DestroyImmediate(parent);
    }

    [Test]
    public void 부모_Transform이_없으면_풀_루트를_부모없이_생성한다()
    {
        var resource = Substitute.For<IResourceService>();

        var sut = new PoolManager(resource, null);

        var root = GameObject.Find("[PoolManager]");

        Assert.IsNotNull(root);
        Assert.IsNull(root.transform.parent);

        sut.Dispose();
    }

    [Test]
    public void Get은_아이템의_로컬_트랜스폼을_보존한다()
    {
        // 원점이 아닌 부모(ScreenSpaceOverlay Canvas의 스크린 좌표계를 모사) 아래에 풀을 둔다.
        var scope = new GameObject("scope");
        scope.transform.position = new Vector3(100f, 200f, 0f);

        var prefab = new GameObject("uiPrefab", typeof(RectTransform));
        prefab.transform.localPosition = Vector3.zero;
        prefab.transform.localScale = Vector3.one;

        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("ui").Returns(prefab);
        var sut = new PoolManager(resource, null, scope.transform);

        var go = sut.Get("ui");

        // worldPositionStays=true면 부모의 월드 위치를 상쇄하려 로컬이 (-100,-200)으로 어긋난다.
        Assert.AreEqual(Vector3.zero, go.transform.localPosition, "Get 후 로컬 위치 보존");
        Assert.AreEqual(Vector3.one, go.transform.localScale, "Get 후 로컬 스케일 보존");

        sut.Dispose();
        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(scope);
    }

    [Test]
    public void Get은_로드_실패시_null을_반환하고_ResourceService에_Release하지_않는다()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("missing").Returns((GameObject)null);
        var sut = new PoolManager(resource, null);

        LogAssert.Expect(LogType.Error, new Regex("Failed to load prefab"));
        var result = sut.Get("missing");

        Assert.IsNull(result);
        resource.DidNotReceive().Release("missing");

        sut.Dispose();
    }

    [Test]
    public void Dispose는_로드한_모든_키를_ResourceService에_Release한다()
    {
        var prefabA = new GameObject("prefabA");
        var prefabB = new GameObject("prefabB");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("a").Returns(prefabA);
        resource.Load<GameObject>("b").Returns(prefabB);
        var sut = new PoolManager(resource, null);
        sut.Get("a");
        sut.Get("b");

        sut.Dispose();

        resource.Received(1).Release("a");
        resource.Received(1).Release("b");

        Object.DestroyImmediate(prefabA);
        Object.DestroyImmediate(prefabB);
    }

    [Test]
    public void Get은_새_인스턴스_생성시_resolver로_컴포넌트에_주입한다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var sut = new PoolManager(resource, resolver);

        sut.Get("enemy");

        // 프리팹에 MonoBehaviour(PoolItem) 1개뿐이라 Inject 호출도 1회
        resolver.Received(1).Inject(Arg.Any<object>());

        sut.Dispose();
        Object.DestroyImmediate(prefab);
    }

    [Test]
    public void 반환된_아이템은_비활성화_후_부모를_풀_루트로_되돌린다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var scope = new GameObject("scope");
        var sut = new PoolManager(resource, null, scope.transform);
        var root = scope.transform.Find("[PoolManager]");

        var customParent = new GameObject("customParent");
        var go = sut.Get("enemy", customParent.transform);

        Assert.AreEqual(customParent.transform, go.transform.parent, "Get 후에는 지정한 부모 아래에 있어야 한다");

        sut.Release(go);

        Assert.IsFalse(go.activeSelf, "Release 후에는 비활성화되어야 한다");
        Assert.AreEqual(root, go.transform.parent, "Release 후에는 풀 루트 아래로 되돌아가야 한다");

        sut.Dispose();
        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(scope);
        Object.DestroyImmediate(customParent);
    }

    [Test]
    public void 재사용된_아이템은_다시_주입하지_않는다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var sut = new PoolManager(resource, resolver);

        var first = sut.Get("enemy");
        sut.Release(first);       // 풀로 반환
        sut.Get("enemy");         // 같은 인스턴스 재사용

        resolver.Received(1).Inject(Arg.Any<object>());

        sut.Dispose();
        Object.DestroyImmediate(prefab);
    }
}
