using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

public class SoundServiceTest
{
    private static AudioClip MakeClip() => AudioClip.Create("clip", 1, 1, 1000, false);

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
        var sut = new SoundService(resource) { SFXEnabled = true };

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
        var sut = new SoundService(resource) { SFXEnabled = true };

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
        var sut = new SoundService(resource) { BGMEnabled = true };

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
        var sut = new SoundService(resource) { SFXEnabled = true, BGMEnabled = true };

        sut.Play("sfx");
        sut.PlayBGM("bgm");
        sut.Dispose();

        resource.Received(1).Release("sfx");
        resource.Received(1).Release("bgm");
    }

    [Test]
    public void 생성_직후_SFX는_활성화_상태다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource);

        Assert.IsTrue(sut.SFXEnabled);

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_활성화_상태다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource);

        Assert.IsTrue(sut.BGMEnabled);

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_재생중이_아니다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource);

        Assert.IsFalse(sut.IsPlayingBGM);

        sut.Dispose();
    }

    [Test]
    public void SFX_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource);
        sut.SFXEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(resource);

        Assert.IsFalse(reloaded.SFXEnabled);

        reloaded.Dispose();
    }

    [Test]
    public void BGM_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource);
        sut.BGMEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(resource);

        Assert.IsFalse(reloaded.BGMEnabled);

        reloaded.Dispose();
    }

    [Test]
    public void BGM_재생중이면_IsPlayingBGM이_true다()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("bgm").Returns(MakeClip());
        var sut = new SoundService(resource);

        sut.PlayBGM("bgm");

        Assert.IsTrue(sut.IsPlayingBGM);

        sut.Dispose();
    }
}
