using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundServiceTest
{
    private static AudioClip MakeClip() => AudioClip.Create("clip", 1, 1, 1000, false);

    private static ISoundCatalog Catalog(params (string key, string resourceKey)[] entries)
    {
        var catalog = Substitute.For<ISoundCatalog>();
        foreach (var (key, resourceKey) in entries)
        {
            var captured = resourceKey;
            catalog.TryGetResourceKey(key, out Arg.Any<string>())
                .Returns(call =>
                {
                    call[1] = captured;
                    return true;
                });
        }
        catalog.Keys.Returns(entries.Select(e => e.key).ToList());
        return catalog;
    }

    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("SFX_ENABLED");
        PlayerPrefs.DeleteKey("BGM_ENABLED");
    }

    [Test]
    public void SFX_재생시_ResourceService에_클립로드를_위임한다()
    {
        var clip = MakeClip();
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("sfx").Returns(clip);
        var sut = new SoundService(resource, Catalog(("sfx", "sfx"))) { SFXEnabled = true };

        sut.Play("sfx");

        resource.Received(1).Load<AudioClip>("sfx");

        sut.Dispose();
    }

    [Test]
    public void 같은_SFX_재생시_클립로드는_한번만_위임한다()
    {
        var clip = MakeClip();
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("sfx").Returns(clip);
        var sut = new SoundService(resource, Catalog(("sfx", "sfx"))) { SFXEnabled = true };

        sut.Play("sfx");
        sut.Play("sfx");

        resource.Received(1).Load<AudioClip>("sfx");

        sut.Dispose();
    }

    [Test]
    public void BGM_재생시_ResourceService에_클립로드를_위임한다()
    {
        var clip = MakeClip();
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("bgm").Returns(clip);
        var sut = new SoundService(resource, Catalog(("bgm", "bgm"))) { BGMEnabled = true };

        sut.PlayBGM("bgm");

        resource.Received(1).Load<AudioClip>("bgm");

        sut.Dispose();
    }

    [Test]
    public void Dispose시_로드한_모든_키를_Release한다()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("sfx").Returns(MakeClip());
        resource.Load<AudioClip>("bgm").Returns(MakeClip());
        var sut = new SoundService(resource, Catalog(("sfx", "sfx"), ("bgm", "bgm")))
            { SFXEnabled = true, BGMEnabled = true };

        sut.Play("sfx");
        sut.PlayBGM("bgm");
        sut.Dispose();

        resource.Received(1).Release("sfx");
        resource.Received(1).Release("bgm");
    }

    [Test]
    public void 카탈로그에_없는_SFX키는_로드하지_않고_에러를_남긴다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog()) { SFXEnabled = true };

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.Play("missing");

        resource.DidNotReceive().Load<AudioClip>(Arg.Any<string>());

        sut.Dispose();
    }

    [Test]
    public void 카탈로그에_없는_BGM키는_로드하지_않고_에러를_남긴다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog()) { BGMEnabled = true };

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.PlayBGM("missing");

        resource.DidNotReceive().Load<AudioClip>(Arg.Any<string>());

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_SFX는_활성화_상태다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog());

        Assert.IsTrue(sut.SFXEnabled);

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_활성화_상태다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog());

        Assert.IsTrue(sut.BGMEnabled);

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_재생중이_아니다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog());

        Assert.IsFalse(sut.IsPlayingBGM);

        sut.Dispose();
    }

    [Test]
    public void SFX_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog());
        sut.SFXEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(resource, Catalog());

        Assert.IsFalse(reloaded.SFXEnabled);

        reloaded.Dispose();
    }

    [Test]
    public void BGM_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog());
        sut.BGMEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(resource, Catalog());

        Assert.IsFalse(reloaded.BGMEnabled);

        reloaded.Dispose();
    }

    [Test]
    public void BGM_재생중이면_IsPlayingBGM이_true다()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("bgm").Returns(MakeClip());
        var sut = new SoundService(resource, Catalog(("bgm", "bgm")));

        sut.PlayBGM("bgm");

        Assert.IsTrue(sut.IsPlayingBGM);

        sut.Dispose();
    }

    [UnityTest]
    public IEnumerator PreloadAsync는_Preload대상_리소스키를_LoadAsync로_로드한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.LoadAsync<AudioClip>("r/a").Returns(UniTask.FromResult(MakeClip()));
        resource.LoadAsync<AudioClip>("r/c").Returns(UniTask.FromResult(MakeClip()));
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadResourceKeys.Returns(new[] { "r/a", "r/c" });
        var sut = new SoundService(resource, catalog);

        await sut.PreloadAsync();

        _ = resource.Received(1).LoadAsync<AudioClip>("r/a");
        _ = resource.Received(1).LoadAsync<AudioClip>("r/c");

        sut.Dispose();
    });

    [UnityTest]
    public IEnumerator PreloadAsync는_중복_리소스키를_한번만_로드한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.LoadAsync<AudioClip>("r/a").Returns(UniTask.FromResult(MakeClip()));
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadResourceKeys.Returns(new[] { "r/a", "r/a" });
        var sut = new SoundService(resource, catalog);

        await sut.PreloadAsync();

        _ = resource.Received(1).LoadAsync<AudioClip>("r/a");

        sut.Dispose();
    });

    [UnityTest]
    public IEnumerator 프리로드_진행중_같은키_재생하면_잉여_참조를_해제한다() => UniTask.ToCoroutine(async () =>
    {
        var clip = MakeClip();
        var source = new UniTaskCompletionSource<AudioClip>();
        var resource = Substitute.For<IResourceService>();
        resource.LoadAsync<AudioClip>("r/a").Returns(source.Task);
        resource.Load<AudioClip>("r/a").Returns(clip);
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadResourceKeys.Returns(new[] { "r/a" });
        catalog.TryGetResourceKey("A", out Arg.Any<string>())
            .Returns(call => { call[1] = "r/a"; return true; });
        var sut = new SoundService(resource, catalog) { SFXEnabled = true };

        var preload = sut.PreloadAsync();   // LoadAsync in-flight (await pending)
        sut.Play("A");                       // 동기 Load로 _table 채움
        source.TrySetResult(clip);           // preload 완료 → 잉여 참조 해제해야 함
        await preload;

        resource.Received(1).Release("r/a");

        sut.Dispose();                       // _table의 r/a 1개 → Release 1회 더
        resource.Received(2).Release("r/a");
    });

    [UnityTest]
    public IEnumerator 프리로드된_키_재생시_추가_Load없이_캐시를_사용한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.LoadAsync<AudioClip>("r/a").Returns(UniTask.FromResult(MakeClip()));
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadResourceKeys.Returns(new[] { "r/a" });
        catalog.TryGetResourceKey("A", out Arg.Any<string>())
            .Returns(call => { call[1] = "r/a"; return true; });
        var sut = new SoundService(resource, catalog) { SFXEnabled = true };

        await sut.PreloadAsync();
        sut.Play("A");

        resource.DidNotReceive().Load<AudioClip>(Arg.Any<string>());

        sut.Dispose();
    });
}
