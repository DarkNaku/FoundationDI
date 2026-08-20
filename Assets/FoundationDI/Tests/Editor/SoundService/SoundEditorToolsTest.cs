using System.IO;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class SoundEditorToolsTest
{
    [TestCase("Jump", true)]
    [TestCase("Jump2", true)]
    [TestCase("", false)]
    [TestCase(null, false)]
    [TestCase("2Jump", false)]
    [TestCase("Jump Sound", false)]
    [TestCase("Jump_Sound", false)]
    [TestCase("점프", false)]
    [TestCase("123", false)]
    public void 유사enum_식별자로_쓸_수_있는_태그만_통과한다(string tag, bool expected)
    {
        Assert.AreEqual(expected, SoundEditorHelper.IsTagValid(tag));
    }

    [Test]
    public void 유사enum_코드는_태그마다_정적_상수를_생성한다()
    {
        string path = Path.Combine(Path.GetTempPath(), "SFX_Generated_Test.cs");

        PseudoEnumGenerator.Generate("SFX", new[] { "Jump", "Land" }, path);

        string generated = File.ReadAllText(path);

        Assert.That(generated, Does.Contain("namespace DarkNaku.FoundationDI"));
        Assert.That(generated, Does.Contain("public partial struct SFX"));
        Assert.That(generated, Does.Contain("public static readonly SFX Jump = new SFX(\"Jump\");"));
        Assert.That(generated, Does.Contain("public static readonly SFX Land = new SFX(\"Land\");"));

        File.Delete(path);
    }

    [Test]
    public void 태그가_없어도_빈_partial_struct를_생성한다()
    {
        string path = Path.Combine(Path.GetTempPath(), "Track_Generated_Test.cs");

        PseudoEnumGenerator.Generate("Track", new string[0], path);

        string generated = File.ReadAllText(path);

        Assert.That(generated, Does.Contain("public partial struct Track"));
        Assert.That(generated, Does.Not.Contain("public static readonly Track "));

        File.Delete(path);
    }

    [TestCase("MyData/Sound", "Assets/MyData/Sound/")]
    [TestCase("Assets/MyData/Sound/", "Assets/MyData/Sound/")]
    [TestCase("Assets//MyData///Sound", "Assets/MyData/Sound/")]
    public void 데이터_루트_경로는_Assets_상대_경로로_정규화된다(string input, string expected)
    {
        bool ok = SoundServiceSettingsWindow.TrySanitizeDataRootPath(input, out string sanitized, out string error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(expected, sanitized);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/absolute/path")]
    [TestCase("C:/Windows")]
    [TestCase("Assets/../Secret")]
    public void 프로젝트_밖을_가리키는_경로는_거부된다(string input)
    {
        bool ok = SoundServiceSettingsWindow.TrySanitizeDataRootPath(input, out _, out string error);

        Assert.IsFalse(ok);
        Assert.IsNotEmpty(error);
    }
}
