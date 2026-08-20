using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundPlaybackTests
{
    private SoundServiceSettings _settings;
    private SoundService _service;

    private static AudioClip MakeClip(string name, float seconds)
    {
        const int frequency = 8000;

        return AudioClip.Create(name, Mathf.CeilToInt(frequency * seconds), 1, frequency, false);
    }

    [SetUp]
    public void SetUp()
    {
        _settings = ScriptableObject.CreateInstance<SoundServiceSettings>();
        _settings.SoundDataCollection = ScriptableObject.CreateInstance<SoundDataCollection>();
        _settings.MusicDataCollection = ScriptableObject.CreateInstance<MusicDataCollection>();
        _settings.OutputDataCollection = ScriptableObject.CreateInstance<OutputDataCollection>();
        _settings.EnableOcclusion = false;

        _settings.SoundDataCollection.CreateSound(new[] { MakeClip("sfx", 1f) }, "Jump",
            CompressionPreset.FrequentSound, false, out _);
        _settings.MusicDataCollection.CreateMusicTrack(new[] { MakeClip("bgm1", 1f) }, "Song1",
            CompressionPreset.AmbientMusic, false, out _);
        _settings.MusicDataCollection.CreateMusicTrack(new[] { MakeClip("bgm2", 1f) }, "Song2",
            CompressionPreset.AmbientMusic, false, out _);

        _service = new SoundService(_settings);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _service.Dispose();

        // Object.Destroy는 프레임 끝에 반영된다. 다음 테스트가 남은 풀 오브젝트를 보지 않도록 한 프레임 기다린다.
        yield return null;

        Object.DestroyImmediate(_settings.SoundDataCollection);
        Object.DestroyImmediate(_settings.MusicDataCollection);
        Object.DestroyImmediate(_settings.OutputDataCollection);
        Object.DestroyImmediate(_settings);
    }

    [UnityTest]
    public IEnumerator 사운드를_재생하면_풀에서_소스를_빌려_재생한다()
    {
        var sound = _service.CreateSound("Jump").SetLoop();

        sound.Play();

        yield return null;

        Assert.IsTrue(sound.Using);
        Assert.IsTrue(sound.Playing);
        Assert.IsNotNull(GameObject.Find("[SoundService] Sources Pool"));
    }

    [UnityTest]
    public IEnumerator 정지하면_소스를_반납하고_사용중이_아니게_된다()
    {
        var sound = _service.CreateSound("Jump").SetLoop();

        sound.Play();

        yield return null;

        sound.Stop();

        yield return null;

        Assert.IsFalse(sound.Using);
    }

    [UnityTest]
    public IEnumerator 두_번째_재생은_반납된_소스를_재사용한다()
    {
        var first = _service.CreateSound("Jump").SetLoop();

        first.Play();

        yield return null;

        first.Stop();

        yield return null;

        var second = _service.CreateSound("Jump").SetLoop();

        second.Play();

        yield return null;

        var poolParent = GameObject.Find("[SoundService] Sources Pool");

        Assert.IsNotNull(poolParent);
        Assert.AreEqual(1, poolParent.transform.childCount);
    }

    [UnityTest]
    public IEnumerator 일시정지와_재개가_동작한다()
    {
        var sound = _service.CreateSound("Jump").SetLoop();

        sound.Play();

        yield return null;

        sound.Pause();

        yield return null;

        Assert.IsTrue(sound.Paused);
        Assert.IsFalse(sound.Playing);

        sound.Resume();

        yield return null;

        Assert.IsFalse(sound.Paused);
        Assert.IsTrue(sound.Playing);
    }

    [UnityTest]
    public IEnumerator id로_참조_없이_정지할_수_있다()
    {
        var sound = _service.CreateSound("Jump").SetLoop().SetId("jump");

        sound.Play();

        yield return null;

        _service.Stop("jump");

        yield return null;

        Assert.IsFalse(sound.Playing);
    }

    [UnityTest]
    public IEnumerator 재생_시작_콜백이_호출된다()
    {
        bool played = false;

        var sound = _service.CreateSound("Jump").SetLoop().OnPlay(() => played = true);

        sound.Play();

        yield return null;

        Assert.IsTrue(played);
    }

    [UnityTest]
    public IEnumerator 플레이리스트는_첫_트랙부터_재생한다()
    {
        var playlist = _service.CreatePlaylist("Song1", "Song2").SetLoop();

        playlist.Play();

        yield return null;

        Assert.IsTrue(playlist.Using);
        Assert.IsTrue(playlist.Playing);
        Assert.IsNotNull(playlist.CurrentPlaylistClip);
        CollectionAssert.AreEqual(new[] { "Song1", "Song2" }, playlist.PlaylistClipsTags);

        playlist.Stop();

        yield return null;
    }

    [UnityTest]
    public IEnumerator 다이내믹_뮤직은_레이어마다_소스를_하나씩_쓴다()
    {
        var dynamicMusic = _service.CreateDynamicMusic("Song1", "Song2").SetLoop();

        dynamicMusic.Play();

        yield return null;

        Assert.IsTrue(dynamicMusic.Playing);

        var poolParent = GameObject.Find("[SoundService] Sources Pool");

        Assert.IsNotNull(poolParent);
        Assert.AreEqual(2, poolParent.transform.childCount);

        dynamicMusic.Stop();

        yield return null;
    }

    [UnityTest]
    public IEnumerator 음악_볼륨을_바꾸면_소스에_반영된다()
    {
        var music = _service.CreateMusic("Song1").SetLoop().SetVolume(1f);

        music.Play();

        yield return null;

        music.ChangeVolume(0.25f);

        yield return null;

        Assert.AreEqual(0.25f, music.Volume, 0.001f);

        music.Stop();

        yield return null;
    }

    [UnityTest]
    public IEnumerator Dispose하면_소스_풀_오브젝트가_사라진다()
    {
        var sound = _service.CreateSound("Jump").SetLoop();

        sound.Play();

        yield return null;

        _service.Dispose();

        yield return null;

        Assert.IsNull(GameObject.Find("[SoundService] Sources Pool"));
    }
}
