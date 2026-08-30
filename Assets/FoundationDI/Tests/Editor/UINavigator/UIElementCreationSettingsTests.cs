using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementCreationSettingsTests
{
    [Test]
    public void 경로_결합은_슬래시_중복을_만들지_않는다()
    {
        Assert.AreEqual("Assets/Scripts/UI/ShopView.cs",
            UIElementCreationSettings.CombineAssetPath("Assets/Scripts/UI/", "ShopView.cs"));
        Assert.AreEqual("Assets/Scripts/UI/ShopView.cs",
            UIElementCreationSettings.CombineAssetPath("Assets/Scripts/UI", "ShopView.cs"));
    }

    [Test]
    public void 경로_결합은_역슬래시를_슬래시로_정규화한다()
    {
        Assert.AreEqual("Assets/Scripts/UI/ShopView.cs",
            UIElementCreationSettings.CombineAssetPath(@"Assets\Scripts\UI", "ShopView.cs"));
    }

    [Test]
    public void 기본값은_Assets_아래_경로로_시작한다()
    {
        var settings = UIElementCreationSettings.instance;

        StringAssert.StartsWith("Assets/", settings.ScriptRoot);
        StringAssert.StartsWith("Assets/", settings.PrefabRoot);
        StringAssert.Contains("Resources", settings.PrefabRoot,
            "기본 백엔드는 ResourcesProvider이므로 기본 프리팹 루트는 Resources 아래여야 한다");
    }
}
