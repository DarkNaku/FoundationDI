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
}
