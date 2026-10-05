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
        var entries = new[]
        {
            new IAPProductEntry("remove_ads", IAPProductType.NonConsumable, default),
            new IAPProductEntry("gems", IAPProductType.Consumable, default),
        };

        var source = IAPProductConstantsGenerator.BuildSource(entries);

        StringAssert.Contains("public static class IAPProducts", source);
        StringAssert.Contains("public const string RemoveAds = \"remove_ads\";", source);
        StringAssert.Contains("public const string Gems = \"gems\";", source);
        StringAssert.Contains("namespace DarkNaku.FoundationDI", source);
    }

    [Test]
    public void 식별자가_충돌하면_하나만_남는다()
    {
        var entries = new[]
        {
            new IAPProductEntry("remove_ads", IAPProductType.NonConsumable, default),
            new IAPProductEntry("remove.ads", IAPProductType.NonConsumable, default),
        };

        var source = IAPProductConstantsGenerator.BuildSource(entries);

        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(source, "RemoveAds").Count);
    }
}
