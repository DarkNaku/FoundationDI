using System.Collections;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Dispose 이후에 들어오는 반환 요청을 PlayMode에서 본다.
///
/// EditMode에서는 재현되지 않는다 - Dispose가 부르는 DestroyImmediate가 즉시 돌아
/// 풀 아이템이 fake-null이 되고 PoolManager.Release의 item == null 가드에서 조기
/// 반환되기 때문이다. PlayMode는 Destroy가 프레임 끝까지 지연되므로 Dispose 직후에도
/// 아이템이 살아 있어 PoolData까지 들어간다.
/// </summary>
public class PoolManagerDisposeTests
{
    [UnityTest]
    public IEnumerator Dispose_후에_들어온_Release는_예외없이_무시된다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var sut = new PoolManager(resource, null);

        var go = sut.Get("enemy");

        sut.Dispose();

        Assert.IsTrue(go != null,
            "플레이 모드에서는 Destroy가 지연되므로 Dispose 직후에도 아이템이 살아 있어야 한다");

        Assert.DoesNotThrow(() => sut.Release(go));

        yield return null;

        Object.DestroyImmediate(prefab);
    }
}
