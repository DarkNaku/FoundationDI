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
}
