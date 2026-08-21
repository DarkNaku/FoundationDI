using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementNamingTests
{
    [TestCase("Shop")]
    [TestCase("ShopPopup")]
    [TestCase("_Shop")]
    [TestCase("Shop2")]
    public void 유효한_이름은_통과한다(string name)
    {
        Assert.IsTrue(UIElementNaming.TryValidate(name, out var error), error);
        Assert.IsEmpty(error);
    }

    [TestCase("", "비어")]
    [TestCase("   ", "비어")]
    [TestCase("2Shop", "숫자")]
    [TestCase("My Shop", "식별자")]
    [TestCase("Shop-1", "식별자")]
    [TestCase("class", "예약어")]
    public void 유효하지_않은_이름은_이유와_함께_거부된다(string name, string reasonKeyword)
    {
        Assert.IsFalse(UIElementNaming.TryValidate(name, out var error));
        StringAssert.Contains(reasonKeyword, error);
    }

    [Test]
    public void Resources_아래_프리팹은_Resources_기준_상대경로가_키가_된다()
    {
        Assert.AreEqual("UI/Shop",
            UIElementNaming.ResolveResourceKey("Assets/Resources/UI/Shop.prefab"));
    }

    [Test]
    public void 중첩된_Resources_폴더도_마지막_Resources를_기준으로_한다()
    {
        Assert.AreEqual("Shop",
            UIElementNaming.ResolveResourceKey("Assets/Game/Resources/Shop.prefab"));
    }

    [Test]
    public void Resources_밖의_프리팹은_경로_전체가_Addressables_주소가_된다()
    {
        Assert.AreEqual("Assets/UI/Shop.prefab",
            UIElementNaming.ResolveResourceKey("Assets/UI/Shop.prefab"));
    }
}
