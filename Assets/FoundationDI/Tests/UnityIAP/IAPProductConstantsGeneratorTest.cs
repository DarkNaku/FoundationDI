using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class IAPProductConstantsGeneratorTest
{
    [Test]
    public void 스네이크_케이스_ID가_파스칼_케이스_식별자가_된다()
    {
        Assert.AreEqual("RemoveAds", IAPProductConstantsGenerator.ToIdentifier("remove_ads"));
        Assert.AreEqual("Gems", IAPProductConstantsGenerator.ToIdentifier("gems"));
        Assert.AreEqual("GemPack100", IAPProductConstantsGenerator.ToIdentifier("gem.pack-100"));
    }

    [Test]
    public void 숫자로_시작하면_밑줄을_붙인다()
    {
        Assert.AreEqual("_100Gems", IAPProductConstantsGenerator.ToIdentifier("100_gems"));
    }

    [Test]
    public void 식별자로_바꿀_수_없으면_null이다()
    {
        Assert.IsNull(IAPProductConstantsGenerator.ToIdentifier(null));
        Assert.IsNull(IAPProductConstantsGenerator.ToIdentifier("   "));
        Assert.IsNull(IAPProductConstantsGenerator.ToIdentifier("___"));
    }

    [Test]
    public void 생성된_소스가_상수를_담는다()
    {
        var source = IAPProductConstantsGenerator.BuildSource(new[] { "remove_ads", "gems" });

        StringAssert.Contains("public static class IAPProducts", source);
        StringAssert.Contains("public const string RemoveAds = \"remove_ads\";", source);
        StringAssert.Contains("public const string Gems = \"gems\";", source);
        StringAssert.Contains("namespace DarkNaku.FoundationDI", source);
    }

    [Test]
    public void 식별자가_충돌하면_하나만_남는다()
    {
        var source = IAPProductConstantsGenerator.BuildSource(new[] { "remove_ads", "remove.ads" });

        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(source, "RemoveAds").Count);
    }

    [Test]
    public void 출력_폴더는_기억한_폴더가_먼저다()
    {
        Assert.AreEqual("Assets/Game/Generated/",
            IAPProductConstantsGenerator.ResolveOutputFolder("Assets/Game/Generated/", "Assets/Old/IAPProducts.cs"));
    }

    [Test]
    public void 기억한_폴더가_없으면_기존_생성_파일의_폴더를_쓴다()
    {
        // 0.10.x가 설정 SO 옆에 만든 파일을 그대로 덮어 써야 게임 코드의 참조가 유지된다.
        Assert.AreEqual("Assets/Settings/Generated/",
            IAPProductConstantsGenerator.ResolveOutputFolder(null, "Assets/Settings/Generated/IAPProducts.cs"));
    }

    [Test]
    public void 둘_다_없으면_null이다()
    {
        Assert.IsNull(IAPProductConstantsGenerator.ResolveOutputFolder("", null));
    }

    [Test]
    public void Assets_밖의_폴더는_기억했어도_쓰지_않는다()
    {
        Assert.IsNull(IAPProductConstantsGenerator.ResolveOutputFolder("/Users/someone/Desktop/", null));
    }
}
