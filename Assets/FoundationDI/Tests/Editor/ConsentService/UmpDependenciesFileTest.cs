using System.IO;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class UmpDependenciesFileTest
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "FdiUmpDeps_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void XML은_UMP_의존성만_선언한다()
    {
        var xml = UmpDependenciesFile.BuildXml();

        StringAssert.Contains($"<androidPackage spec=\"{UmpDependenciesFile.AndroidPackage}\"", xml);
        StringAssert.Contains($"<iosPod name=\"{UmpDependenciesFile.IosPod}\" version=\"{UmpDependenciesFile.IosPodVersion}\"", xml);

        // 광고 SDK를 끌고 오면 LevelPlay AdMob 어댑터(Next-Gen)와 충돌한다.
        StringAssert.DoesNotContain("play-services-ads", xml);
        StringAssert.DoesNotContain("Google-Mobile-Ads-SDK", xml);
    }

    [Test]
    public void 하나라도_UMP를_켜면_파일이_있어야_한다()
    {
        Assert.IsTrue(UmpDependenciesFile.ShouldExist(new[] { false, true }));
        Assert.IsFalse(UmpDependenciesFile.ShouldExist(new[] { false, false }));
        Assert.IsFalse(UmpDependenciesFile.ShouldExist(new bool[0]));
    }

    [Test]
    public void 켜면_파일을_만들고_끄면_지운다()
    {
        var path = Path.Combine(_root, UmpDependenciesFile.AssetPath);

        Assert.IsTrue(UmpDependenciesFile.Sync(true, _root));
        Assert.IsTrue(File.Exists(path));

        Assert.IsFalse(UmpDependenciesFile.Sync(true, _root), "내용이 같으면 다시 쓰지 않는다(재임포트 루프 방지)");

        Assert.IsTrue(UmpDependenciesFile.Sync(false, _root));
        Assert.IsFalse(File.Exists(path));
    }
}
