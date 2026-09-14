using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine.Pool;

public class PoolDataTest
{
    [Test]
    public void Clear된_뒤에_들어온_Release는_예외없이_무시된다()
    {
        var pool = Substitute.For<IObjectPool<IPoolItem>>();
        var item = Substitute.For<IPoolItem>();
        var sut = new PoolData(pool);

        sut.Clear();

        // IPoolItem.Release가 public이라 PoolManager를 거치지 않고 아이템이 직접
        // 반환을 시도할 수 있다. 정리된 풀은 그 요청을 스스로 거부해야 한다.
        Assert.DoesNotThrow(() => sut.Release(item));

        pool.DidNotReceive().Release(item);
    }
}
