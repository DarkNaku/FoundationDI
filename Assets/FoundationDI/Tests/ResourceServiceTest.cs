using System.Collections;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ResourceServiceTest
{
    [UnityTest]
    public IEnumerator 첫_로드시_provider를_호출하고_에셋을_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        var result = await sut.LoadAsync<GameObject>("key");

        Assert.AreEqual(asset, result);
        _ = provider.Received(1).LoadAsync<GameObject>("key");
    });

    [UnityTest]
    public IEnumerator 같은_키_재로드시_provider를_다시_호출하지_않고_캐시에서_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        var first = await sut.LoadAsync<GameObject>("key");
        var second = await sut.LoadAsync<GameObject>("key");

        Assert.AreEqual(asset, second);
        Assert.AreEqual(first, second);
        _ = provider.Received(1).LoadAsync<GameObject>("key");
    });

    [UnityTest]
    public IEnumerator Release시_참조가_남아있으면_provider_Release를_호출하지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");
        await sut.LoadAsync<GameObject>("key");   // RefCount = 2
        sut.Release("key");                        // RefCount = 1

        provider.DidNotReceive().Release("key");
    });

    [UnityTest]
    public IEnumerator 참조가_0이_되면_provider_Release를_호출한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");   // RefCount = 1
        sut.Release("key");                        // RefCount = 0

        provider.Received(1).Release("key");
    });

    [UnityTest]
    public IEnumerator 해제된_키를_다시_로드하면_provider를_재호출한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");   // RefCount = 1
        sut.Release("key");                        // RefCount = 0, 해제
        await sut.LoadAsync<GameObject>("key");    // 재로드

        _ = provider.Received(2).LoadAsync<GameObject>("key");
    });

    [UnityTest]
    public IEnumerator 보유_참조보다_많이_Release하면_안전하게_무시한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");   // RefCount = 1
        sut.Release("key");                        // 0, 제거됨

        Assert.DoesNotThrow(() => sut.Release("key"));   // 추가 Release는 무시
        provider.Received(1).Release("key");
    });

    [UnityTest]
    public IEnumerator Dispose시_남은_모든_키의_핸들을_해제한다() => UniTask.ToCoroutine(async () =>
    {
        var assetA = new GameObject("assetA");
        var assetB = new GameObject("assetB");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("a").Returns(UniTask.FromResult(assetA));
        provider.LoadAsync<GameObject>("b").Returns(UniTask.FromResult(assetB));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("a");
        await sut.LoadAsync<GameObject>("b");
        sut.Dispose();

        provider.Received(1).Release("a");
        provider.Received(1).Release("b");
    });

    [UnityTest]
    public IEnumerator 로드_진행중_재호출시_provider를_중복_호출하지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var source = new UniTaskCompletionSource<GameObject>();
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(source.Task);
        var sut = new ResourceService(provider);

        var t1 = sut.LoadAsync<GameObject>("key");   // 로드 시작 (in-flight)
        var t2 = sut.LoadAsync<GameObject>("key");   // 진행 중 task 재사용해야 함
        source.TrySetResult(asset);
        await UniTask.WhenAll(t1, t2);

        _ = provider.Received(1).LoadAsync<GameObject>("key");
    });

    [Test]
    public void 동기_Load도_동일한_참조_카운팅을_따른다()
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.Load<GameObject>("key").Returns(asset);
        var sut = new ResourceService(provider);

        var first = sut.Load<GameObject>("key");
        var second = sut.Load<GameObject>("key");   // RefCount = 2, 캐시 히트
        sut.Release("key");                          // RefCount = 1
        sut.Release("key");                          // RefCount = 0, 해제

        Assert.AreEqual(asset, first);
        Assert.AreEqual(first, second);
        provider.Received(1).Load<GameObject>("key");
        provider.Received(1).Release("key");
    }

    [Test]
    public void 동기_로드_실패시_캐시하지_않아_다음_로드는_provider를_다시_호출한다()
    {
        var provider = Substitute.For<IResourceProvider>();
        provider.Load<GameObject>("missing").Returns((GameObject)null);
        var sut = new ResourceService(provider);

        var first = sut.Load<GameObject>("missing");
        var second = sut.Load<GameObject>("missing");

        Assert.IsNull(first);
        Assert.IsNull(second);
        provider.Received(2).Load<GameObject>("missing");   // 캐시 안 됨 → 매번 provider 호출
    }

    [Test]
    public void 동기_로드_실패시_provider_Release로_핸들을_정리한다()
    {
        var provider = Substitute.For<IResourceProvider>();
        provider.Load<GameObject>("missing").Returns((GameObject)null);
        var sut = new ResourceService(provider);

        sut.Load<GameObject>("missing");

        provider.Received(1).Release("missing");
    }

    [UnityTest]
    public IEnumerator 비동기_로드_실패시_캐시하지_않아_다음_로드는_provider를_다시_호출한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("missing").Returns(_ => UniTask.FromResult<GameObject>(null));
        var sut = new ResourceService(provider);

        var first = await sut.LoadAsync<GameObject>("missing");
        var second = await sut.LoadAsync<GameObject>("missing");

        Assert.IsNull(first);
        Assert.IsNull(second);
        _ = provider.Received(2).LoadAsync<GameObject>("missing");
    });
}
