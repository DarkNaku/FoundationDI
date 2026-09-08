using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 2D 사운드의 같은 프레임 중복 재생 방지. 2D는 거리 감쇠가 없어 같은 클립이 같은 프레임에
/// 겹치면 진폭이 정확히 2배가 되고 출력 단계에서 클리핑된다.
/// </summary>
public class SoundFrameDedupeTest
{
    /// <summary>EditMode에서는 Time.frameCount를 테스트가 제어할 수 없어 seam으로 대체한다.</summary>
    private sealed class FakeFrameClock : ISoundFrameClock
    {
        public int FrameCount { get; set; }
    }

    private SoundServiceSettings _settings;
    private FakeFrameClock _clock;
    private SoundService _service;
    private ISoundEngine _engine;

    private AudioClip _clipA;
    private AudioClip _clipB;

    private static AudioClip MakeClip(string name)
    {
        const int frequency = 8000;

        return AudioClip.Create(name, frequency, 1, frequency, false);
    }

    /// <summary>풀이 만든 소스 개수. 스킵되면 늘지 않는다.</summary>
    private static int PooledSourceCount()
    {
        var pool = GameObject.Find("[SoundService] Sources Pool");

        return pool == null ? 0 : pool.transform.childCount;
    }

    [SetUp]
    public void SetUp()
    {
        _settings = ScriptableObject.CreateInstance<SoundServiceSettings>();
        _settings.SoundDataCollection = ScriptableObject.CreateInstance<SoundDataCollection>();
        _settings.MusicDataCollection = ScriptableObject.CreateInstance<MusicDataCollection>();
        _settings.OutputDataCollection = ScriptableObject.CreateInstance<OutputDataCollection>();
        _settings.EnableOcclusion = false;

        _settings.SoundDataCollection.CreateSound(new[] { MakeClip("click") }, "Click",
            CompressionPreset.FrequentSound, false, out _);
        _settings.SoundDataCollection.CreateSound(new[] { MakeClip("hit") }, "Hit",
            CompressionPreset.FrequentSound, false, out _);

        _clock = new FakeFrameClock { FrameCount = 1 };
        _service = new SoundService(_settings, new PlayerPrefsVolumeStorage(), _clock);
        _engine = _service;

        _clipA = AudioClip.Create("A", 64, 1, 44100, false);
        _clipB = AudioClip.Create("B", 64, 1, 44100, false);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        Object.DestroyImmediate(_clipA);
        Object.DestroyImmediate(_clipB);
        Object.DestroyImmediate(_settings.SoundDataCollection);
        Object.DestroyImmediate(_settings.MusicDataCollection);
        Object.DestroyImmediate(_settings.OutputDataCollection);
        Object.DestroyImmediate(_settings);
    }

    [Test]
    public void 같은_프레임에_같은_클립은_한_번만_예약된다()
    {
        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipA), "첫 재생은 허용돼야 한다.");
        Assert.IsFalse(_engine.TryReserveClipThisFrame(_clipA), "같은 프레임의 두 번째는 막혀야 한다.");
        Assert.IsFalse(_engine.TryReserveClipThisFrame(_clipA), "세 번째도 막혀야 한다.");
    }

    [Test]
    public void 프레임이_바뀌면_같은_클립을_다시_예약할_수_있다()
    {
        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipA));
        Assert.IsFalse(_engine.TryReserveClipThisFrame(_clipA));

        _clock.FrameCount++;

        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipA), "다음 프레임에서는 다시 허용돼야 한다.");
    }

    [Test]
    public void 클립이_다르면_같은_프레임에도_함께_예약된다()
    {
        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipA));
        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipB), "다른 클립은 막히면 안 된다.");
    }

    [Test]
    public void 클립이_null이면_예약을_막지_않는다()
    {
        // 태그 조회 실패 경로. 여기서 막으면 원래의 경고 로그 경로가 가려진다.
        Assert.IsTrue(_engine.TryReserveClipThisFrame(null));
        Assert.IsTrue(_engine.TryReserveClipThisFrame(null));
    }

    [Test]
    public void 프레임이_되돌아가도_직전_프레임_기록은_남지_않는다()
    {
        _clock.FrameCount = 10;
        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipA));

        // 프레임 값이 같지 않기만 하면 슬롯이 비워져야 한다(== 비교, > 비교가 아니다).
        _clock.FrameCount = 9;

        Assert.IsTrue(_engine.TryReserveClipThisFrame(_clipA));
    }

    // --- Sound.Play() 통합 ---

    [Test]
    public void 이D_사운드는_같은_프레임에_같은_클립을_겹쳐_재생하지_않는다()
    {
        var sound = _service.CreateSound("Click").SetSpatialSound(false);

        sound.Play();
        sound.Play();

        Assert.AreEqual(1, PooledSourceCount(), "두 번째 재생이 스킵돼 소스가 하나만 쓰여야 한다.");
    }

    [Test]
    public void 삼D_사운드는_같은_프레임에_같은_클립을_겹쳐_재생한다()
    {
        // 3D는 거리 감쇠와 패닝이 있어 위상이 그대로 겹치지 않는다. 기존 동작을 유지한다.
        var sound = _service.CreateSound("Click").SetSpatialSound(true);

        sound.Play();
        sound.Play();

        Assert.AreEqual(2, PooledSourceCount(), "3D는 겹쳐 재생돼야 한다.");
    }

    [Test]
    public void 이D_사운드도_프레임이_바뀌면_다시_재생한다()
    {
        var sound = _service.CreateSound("Click").SetSpatialSound(false);

        sound.Play();

        _clock.FrameCount++;

        sound.Play();

        Assert.AreEqual(2, PooledSourceCount(), "다음 프레임에서는 새 소스를 빌려야 한다.");
    }

    [Test]
    public void 이D라도_클립이_다르면_같은_프레임에_함께_재생한다()
    {
        var click = _service.CreateSound("Click").SetSpatialSound(false);
        var hit = _service.CreateSound("Hit").SetSpatialSound(false);

        click.Play();
        hit.Play();

        Assert.AreEqual(2, PooledSourceCount(), "다른 클립은 막히면 안 된다.");
    }

    [Test]
    public void 서로_다른_Sound_인스턴스도_같은_클립이면_스킵된다()
    {
        // 버튼 두 개가 같은 클릭음을 같은 프레임에 내는 경우. 인스턴스별 판정으로는 못 막는다.
        var first = _service.CreateSound("Click").SetSpatialSound(false);
        var second = _service.CreateSound("Click").SetSpatialSound(false);

        first.Play();
        second.Play();

        Assert.AreEqual(1, PooledSourceCount(), "클립 단위 전역 판정이라 두 번째는 막혀야 한다.");
    }

    [Test]
    public void 루프_이D_사운드는_같은_프레임_재시작이_막히지_않는다()
    {
        // 루프의 Play()는 이전 재생을 Stop하고 교체한다. 여기서 스킵하면 정지만 되고
        // 새 재생이 막혀 소리가 통째로 사라진다.
        var sound = _service.CreateSound("Click").SetSpatialSound(false).SetLoop();

        sound.Play();
        sound.Play();

        Assert.IsTrue(sound.Using, "재시작 후에도 소스를 들고 있어야 한다.");
    }
}
