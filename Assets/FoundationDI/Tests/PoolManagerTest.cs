using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class PoolManagerTest
{
    [Test]
    public void Get은_프리팹_로드를_ResourceService에_위임한다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var sut = new PoolManager(resource);

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
        var sut = new PoolManager(resource);

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
        var sut = new PoolManager(resource, parent.transform);

        var root = parent.transform.Find("[PoolManager]");

        Assert.IsNotNull(root);

        sut.Dispose();
        Object.DestroyImmediate(parent);
    }

    [Test]
    public void 부모_Transform이_없으면_풀_루트를_부모없이_생성한다()
    {
        var resource = Substitute.For<IResourceService>();

        var sut = new PoolManager(resource);

        var root = GameObject.Find("[PoolManager]");

        Assert.IsNotNull(root);
        Assert.IsNull(root.transform.parent);

        sut.Dispose();
    }

    [Test]
    public void Get은_로드_실패시_null을_반환하고_ResourceService에_Release하지_않는다()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("missing").Returns((GameObject)null);
        var sut = new PoolManager(resource);

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
        var sut = new PoolManager(resource);
        sut.Get("a");
        sut.Get("b");

        sut.Dispose();

        resource.Received(1).Release("a");
        resource.Received(1).Release("b");

        Object.DestroyImmediate(prefabA);
        Object.DestroyImmediate(prefabB);
    }
}
