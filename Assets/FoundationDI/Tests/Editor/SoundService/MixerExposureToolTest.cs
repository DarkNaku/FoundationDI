using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 믹서 노출 자동화 검증. 유니티가 공개 API를 주지 않아 <see cref="MixerExposureTool"/>이
/// UnityEditor.Audio 내부 타입을 리플렉션으로 다루므로, 실제 .mixer 에셋을 만들어
/// 런타임 API(<c>AudioMixer.GetFloat</c>)가 노출을 인식하는지까지 확인한다.
/// </summary>
public class MixerExposureToolTest
{
    private const string TestFolder = "Assets/__MixerExposureToolTest";

    [SetUp]
    public void SetUp()
    {
        if (!AssetDatabase.IsValidFolder(TestFolder))
        {
            AssetDatabase.CreateFolder("Assets", "__MixerExposureToolTest");
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (AssetDatabase.IsValidFolder(TestFolder))
        {
            AssetDatabase.DeleteAsset(TestFolder);
        }
    }

    private static string PathFor(string fileName) => TestFolder + "/" + fileName + ".mixer";

    [Test]
    public void 믹서_내부_API_리플렉션_바인딩이_성립한다()
    {
        Assert.IsTrue(MixerExposureTool.IsAvailable,
            "UnityEditor.Audio 내부 타입 바인딩에 실패했습니다. 유니티 버전 변경으로 API가 바뀌었을 수 있습니다.");
    }

    [Test]
    public void 기본_믹서를_만들면_Master_아래에_BGM과_SFX_그룹이_생긴다()
    {
        var mixer = MixerExposureTool.CreateDefaultMixer(PathFor("Default"), new[] { "BGM", "SFX" });

        Assert.IsNotNull(mixer);

        var names = System.Array.ConvertAll(mixer.FindMatchingGroups(null), group => group.name);

        Assert.That(names, Does.Contain("Master"));
        Assert.That(names, Does.Contain("BGM"));
        Assert.That(names, Does.Contain("SFX"));

        var bgm = mixer.FindMatchingGroups("BGM");

        Assert.AreEqual(1, bgm.Length, "BGM 그룹이 Master 아래에 하나만 있어야 한다.");
    }

    [Test]
    public void 그룹_Volume을_노출하면_GetFloat이_그룹명으로_성공한다()
    {
        var mixer = MixerExposureTool.CreateDefaultMixer(PathFor("Exposed"), new[] { "BGM", "SFX" });

        // 에셋에 실제로 기록됐는지 보기 위해 디스크에서 다시 읽는다.
        var reloaded = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetDatabase.GetAssetPath(mixer));

        foreach (var name in new[] { "Master", "BGM", "SFX" })
        {
            Assert.IsTrue(reloaded.GetFloat(name, out _),
                $"'{name}' 노출 파라미터를 런타임에서 찾지 못했습니다. 파라미터 이름이 그룹명과 다릅니다.");
        }
    }

    [Test]
    public void 공백이_있는_그룹명은_공백을_제거한_이름으로_노출된다()
    {
        var mixer = MixerExposureTool.CreateDefaultMixer(PathFor("Spaced"), new[] { "UI Sound" });

        Assert.IsTrue(mixer.GetFloat("UISound", out _), "공백을 제거한 이름으로 노출돼야 한다.");
        Assert.IsFalse(mixer.GetFloat("UI Sound", out _), "공백이 있는 이름은 쓰이지 않아야 한다.");
    }

    [Test]
    public void 이미_올바르게_노출된_그룹은_다시_노출하지_않는다()
    {
        var mixer = MixerExposureTool.CreateDefaultMixer(PathFor("Idempotent"), new[] { "BGM" });

        var report = MixerExposureTool.ExposeAllGroupVolumes(mixer);

        Assert.AreEqual(0, report.Exposed.Count, "새로 노출한 그룹이 없어야 한다.");
        Assert.AreEqual(0, report.Renamed.Count, "이름을 바꾼 그룹이 없어야 한다.");
        Assert.That(report.AlreadyCorrect, Does.Contain("Master"));
        Assert.That(report.AlreadyCorrect, Does.Contain("BGM"));
        Assert.IsFalse(report.HasChanges);
    }

    [Test]
    public void 믹서가_없는_설정에는_기본_믹서와_기본_Output을_채워_준다()
    {
        var settings = ScriptableObject.CreateInstance<SoundServiceSettings>();

        try
        {
            bool created = SoundServiceAssetLocator.TryCreateDefaultMixer(settings, TestFolder + "/");

            Assert.IsTrue(created);
            Assert.IsNotNull(settings.MasterAudioMixer, "믹서가 설정에 연결돼야 한다.");

            // 기본 Output이 비면 믹서를 통째로 우회해 볼륨 설정이 안 먹는다.
            Assert.IsFalse(settings.DefaultOutput.IsNull, "기본 Output이 채워져야 한다.");
            Assert.AreEqual("SFX", settings.DefaultOutput.ToString());

            Assert.IsTrue(settings.MasterAudioMixer.GetFloat("SFX", out _),
                "기본 Output에 해당하는 노출 파라미터가 있어야 한다.");
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 믹서가_이미_지정된_설정에는_기본_믹서를_만들지_않는다()
    {
        var settings = ScriptableObject.CreateInstance<SoundServiceSettings>();

        try
        {
            var existing = MixerExposureTool.CreateDefaultMixer(PathFor("Existing"), new[] { "BGM" });

            settings.MasterAudioMixer = existing;

            bool created = SoundServiceAssetLocator.TryCreateDefaultMixer(settings, TestFolder + "/");

            Assert.IsFalse(created, "이미 믹서가 지정돼 있으면 만들지 않아야 한다.");
            Assert.AreSame(existing, settings.MasterAudioMixer, "기존 믹서를 바꿔치기하면 안 된다.");
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }
}
