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

    private static ISoundCatalog Catalog(params (string key, AudioClip clip)[] entries)
    {
        var catalog = Substitute.For<ISoundCatalog>();
        foreach (var (key, clip) in entries)
        {
            var captured = clip;
            catalog.TryGetClip(key, out Arg.Any<AudioClip>())
                .Returns(call => { call[1] = captured; return true; });
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
    public void SFX_재생시_카탈로그에서_클립을_가져온다()
    {
        var catalog = Catalog(("sfx", MakeClip()));
        var sut = new SoundService(catalog) { SFXEnabled = true };

        sut.Play("sfx");

        catalog.Received(1).TryGetClip("sfx", out Arg.Any<AudioClip>());

        sut.Dispose();
    }

    [Test]
    public void 같은_프레임_SFX는_클립을_한번만_조회한다()
    {
        var catalog = Catalog(("sfx", MakeClip()));
        var sut = new SoundService(catalog) { SFXEnabled = true };

        sut.Play("sfx");
        sut.Play("sfx");

        // 프레임 중복 차단이 카탈로그 조회 전에 걸리므로 조회는 1회.
        catalog.Received(1).TryGetClip("sfx", out Arg.Any<AudioClip>());

        sut.Dispose();
    }

    [Test]
    public void BGM_재생시_카탈로그에서_클립을_가져온다()
    {
        var catalog = Catalog(("bgm", MakeClip()));
        var sut = new SoundService(catalog) { BGMEnabled = true };

        sut.PlayBGM("bgm");

        catalog.Received(1).TryGetClip("bgm", out Arg.Any<AudioClip>());

        sut.Dispose();
    }

    [Test]
    public void 카탈로그에_없는_SFX키는_재생하지_않고_에러를_남긴다()
    {
        var catalog = Catalog();
        var sut = new SoundService(catalog) { SFXEnabled = true };

        LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.Play("missing");

        sut.Dispose();
    }

    [Test]
    public void 카탈로그에_없는_BGM키는_재생하지_않고_에러를_남긴다()
    {
        var catalog = Catalog();
        var sut = new SoundService(catalog) { BGMEnabled = true };

        LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.PlayBGM("missing");

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_SFX는_활성화_상태다()
    {
        var sut = new SoundService(Catalog());
        Assert.IsTrue(sut.SFXEnabled);
        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_활성화_상태다()
    {
        var sut = new SoundService(Catalog());
        Assert.IsTrue(sut.BGMEnabled);
        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_재생중이_아니다()
    {
        var sut = new SoundService(Catalog());
        Assert.IsFalse(sut.IsPlayingBGM);
        sut.Dispose();
    }

    [Test]
    public void SFX_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var sut = new SoundService(Catalog());
        sut.SFXEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(Catalog());
        Assert.IsFalse(reloaded.SFXEnabled);
        reloaded.Dispose();
    }

    [Test]
    public void BGM_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var sut = new SoundService(Catalog());
        sut.BGMEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(Catalog());
        Assert.IsFalse(reloaded.BGMEnabled);
        reloaded.Dispose();
    }

    [Test]
    public void BGM_재생중이면_IsPlayingBGM이_true다()
    {
        var catalog = Catalog(("bgm", MakeClip()));
        var sut = new SoundService(catalog);

        sut.PlayBGM("bgm");

        Assert.IsTrue(sut.IsPlayingBGM);

        sut.Dispose();
    }

    [UnityTest]
    public IEnumerator PreloadAsync는_카탈로그의_PreloadClips를_열거한다() => UniTask.ToCoroutine(async () =>
    {
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadClips.Returns(new[] { MakeClip(), MakeClip() });
        var sut = new SoundService(catalog);

        await sut.PreloadAsync();

        _ = catalog.Received(1).PreloadClips;

        sut.Dispose();
    });
}
