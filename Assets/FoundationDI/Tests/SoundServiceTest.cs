using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundServiceTest
{
    private sealed class FakeVolumeStorage : ISoundVolumeStorage
    {
        public readonly Dictionary<string, float> Values = new();
        public int SaveCount;

        public bool HasKey(string key) => Values.ContainsKey(key);

        public float GetFloat(string key, float defaultValue) =>
            Values.TryGetValue(key, out float value) ? value : defaultValue;

        public void SetFloat(string key, float value) => Values[key] = value;

        public void Save() => SaveCount++;
    }

    private SoundServiceSettings _settings;
    private FakeVolumeStorage _storage;
    private SoundService _service;

    [SetUp]
    public void SetUp()
    {
        _settings = ScriptableObject.CreateInstance<SoundServiceSettings>();
        _settings.SoundDataCollection = ScriptableObject.CreateInstance<SoundDataCollection>();
        _settings.MusicDataCollection = ScriptableObject.CreateInstance<MusicDataCollection>();
        _settings.OutputDataCollection = ScriptableObject.CreateInstance<OutputDataCollection>();

        _storage = new FakeVolumeStorage();
        _service = new SoundService(_settings, _storage);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        Object.DestroyImmediate(_settings.SoundDataCollection);
        Object.DestroyImmediate(_settings.MusicDataCollection);
        Object.DestroyImmediate(_settings.OutputDataCollection);
        Object.DestroyImmediate(_settings);
    }

    [Test]
    public void 저장된_적_없는_Output_볼륨은_1로_초기화된다()
    {
        float volume = _service.GetSavedOutputVolume("Master");

        Assert.AreEqual(1f, volume);
        Assert.AreEqual(1f, _storage.GetFloat("Master", -1f));
    }

    [Test]
    public void 저장된_Output_볼륨을_그대로_반환한다()
    {
        _storage.SetFloat("BGM", 0.42f);

        Assert.AreEqual(0.42f, _service.GetSavedOutputVolume("BGM"), 0.0001f);
    }

    [Test]
    public void 없는_Output_볼륨을_바꾸면_에러를_남기고_저장하지_않는다()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Missing.*"));
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Missing.*"));

        _service.ChangeOutputVolume("Missing", 0.5f);

        Assert.IsFalse(_storage.HasKey("Missing"));
    }

    [Test]
    public void 팩토리는_각각_새_빌더를_돌려준다()
    {
        var sound = _service.CreateSound("Jump");
        var music = _service.CreateMusic("Theme");
        var playlist = _service.CreatePlaylist("A", "B");
        var dynamicMusic = _service.CreateDynamicMusic("A", "B");

        Assert.IsNotNull(sound);
        Assert.IsNotNull(music);
        Assert.IsNotNull(playlist);
        Assert.IsNotNull(dynamicMusic);
        Assert.AreNotSame(sound, _service.CreateSound("Jump"));
    }

    [Test]
    public void 재생하지_않은_빌더는_사용중이_아니다()
    {
        var sound = _service.CreateSound("Jump");

        Assert.IsFalse(sound.Using);
        Assert.IsFalse(sound.Playing);
        Assert.IsFalse(sound.Paused);
        Assert.AreEqual(0f, sound.PlayingTime);
    }

    [Test]
    public void 없는_id를_정지하면_경고만_남긴다()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*ghost.*"));

        Assert.DoesNotThrow(() => _service.Stop("ghost"));
    }

    [Test]
    public void Dispose는_여러_번_호출해도_안전하다()
    {
        Assert.DoesNotThrow(() =>
        {
            _service.Dispose();
            _service.Dispose();
        });
    }
}
