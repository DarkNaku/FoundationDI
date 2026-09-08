using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine;

public class SoundServiceRegistrationTest
{
    private SoundServiceSettings _settings;

    [SetUp]
    public void SetUp() => _settings = ScriptableObject.CreateInstance<SoundServiceSettings>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_settings);

    [Test]
    public void ISoundService와_ISoundEngine은_같은_인스턴스를_돌려준다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSoundService(_settings);

        using var container = builder.Build();

        // 빌더 API(ISoundService)와 내부 seam(ISoundEngine)이 같은 서비스를 봐야 한다.
        // 계약마다 별도 인스턴스가 생기면 재생 상태가 둘로 갈린다.
        Assert.AreSame(container.Resolve<ISoundService>(), container.Resolve<ISoundEngine>());
    }

    [Test]
    public void 설정_에셋이_그대로_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSoundService(_settings);

        using var container = builder.Build();

        Assert.AreSame(_settings, container.Resolve<SoundServiceSettings>());
    }

    [Test]
    public void 볼륨_저장소는_PlayerPrefs_기본_구현으로_등록된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSoundService(_settings);

        using var container = builder.Build();

        Assert.IsInstanceOf<PlayerPrefsVolumeStorage>(container.Resolve<ISoundVolumeStorage>());
    }
}
