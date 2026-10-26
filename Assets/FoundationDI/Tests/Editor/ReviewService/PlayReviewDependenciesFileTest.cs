using System.IO;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class PlayReviewDependenciesFileTest
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "FdiReviewDeps_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void XML은_Play_In_App_Review만_선언한다()
    {
        var xml = PlayReviewDependenciesFile.BuildXml();

        StringAssert.Contains($"<androidPackage spec=\"{PlayReviewDependenciesFile.AndroidPackage}\"", xml);
        StringAssert.DoesNotContain("<iosPods>", xml, "iOS는 StoreKit(OS 내장)이라 의존성이 없다");
    }

    [Test]
    public void 켜면_파일을_만들고_끄면_지운다()
    {
        var path = Path.Combine(_root, PlayReviewDependenciesFile.AssetPath);

        Assert.IsTrue(PlayReviewDependenciesFile.Sync(true, _root));
        Assert.IsTrue(File.Exists(path));

        Assert.IsFalse(PlayReviewDependenciesFile.Sync(true, _root), "내용이 같으면 다시 쓰지 않는다(재임포트 루프 방지)");

        Assert.IsTrue(PlayReviewDependenciesFile.Sync(false, _root));
        Assert.IsFalse(File.Exists(path));
    }
}
