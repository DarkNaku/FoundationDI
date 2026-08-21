using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementTemplatesTests
{
    [Test]
    public void View_템플릿은_UIView를_상속한_클래스를_만든다()
    {
        var code = UIElementTemplates.View("MyGame.UI", "Shop");

        StringAssert.Contains("namespace MyGame.UI", code);
        StringAssert.Contains("public class ShopView : UIView", code);
        StringAssert.Contains("using DarkNaku.FoundationDI;", code);
    }

    [Test]
    public void 네임스페이스가_비면_네임스페이스_블록_없이_만든다()
    {
        var code = UIElementTemplates.View("", "Shop");

        StringAssert.DoesNotContain("namespace", code);
        StringAssert.Contains("public class ShopView : UIView", code);
    }

    [TestCase(UIElementMode.Page, "UIPagePresenter<ShopView>")]
    [TestCase(UIElementMode.Popup, "UIPopupPresenter<ShopView>")]
    [TestCase(UIElementMode.Overlay, "UIOverlayPresenter<ShopView>")]
    public void Presenter_템플릿은_모드에_맞는_기반_클래스를_쓴다(UIElementMode mode, string expectedBase)
    {
        var code = UIElementTemplates.Presenter("MyGame.UI", "Shop", mode, "UI/Shop");

        StringAssert.Contains($"public class ShopPresenter : {expectedBase}", code);
    }

    [Test]
    public void Presenter_템플릿은_로드_키를_UIPrefab_속성으로_붙인다()
    {
        var code = UIElementTemplates.Presenter("MyGame.UI", "Shop", UIElementMode.Popup, "UI/Shop");

        StringAssert.Contains("[UIPrefab(\"UI/Shop\")]", code);
    }

    [Test]
    public void Presenter_템플릿은_OnInitialize_오버라이드_자리를_남긴다()
    {
        var code = UIElementTemplates.Presenter("MyGame.UI", "Shop", UIElementMode.Page, "UI/Shop");

        StringAssert.Contains("protected override void OnInitialize()", code);
    }
}
