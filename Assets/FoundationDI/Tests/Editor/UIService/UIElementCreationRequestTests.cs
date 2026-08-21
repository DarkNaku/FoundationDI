using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementCreationRequestTests
{
    [Test]
    public void 요청은_JSON_왕복에서_모든_필드를_보존한다()
    {
        var original = new UIElementCreationRequest
        {
            Name = "Shop",
            Mode = UIElementMode.Popup,
            Namespace = "MyGame.UI",
            PrefabPath = "Assets/Resources/UI/Shop.prefab",
        };

        var restored = UIElementCreationRequest.FromJson(original.ToJson());

        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.Mode, restored.Mode);
        Assert.AreEqual(original.Namespace, restored.Namespace);
        Assert.AreEqual(original.PrefabPath, restored.PrefabPath);
    }

    [Test]
    public void 잘못된_JSON은_null을_돌려준다()
    {
        Assert.IsNull(UIElementCreationRequest.FromJson(""));
        Assert.IsNull(UIElementCreationRequest.FromJson("not json"));
    }
}
