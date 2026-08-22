using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class IapEntitlementStorageTest
{
    private const string ProductId = "foundationdi_test_remove_ads";

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey($"FoundationDI.IAP.Owned.{ProductId}");

    [Test]
    public void 저장한_소유_상태가_새_인스턴스에서도_읽힌다()
    {
        var storage = new PlayerPrefsEntitlementStorage();

        Assert.IsFalse(storage.IsOwned(ProductId));

        storage.SetOwned(ProductId, true);
        Assert.IsTrue(new PlayerPrefsEntitlementStorage().IsOwned(ProductId));

        storage.SetOwned(ProductId, false);
        Assert.IsFalse(new PlayerPrefsEntitlementStorage().IsOwned(ProductId));
    }

    [Test]
    public void 빈_상품_ID는_소유가_아니고_저장도_하지_않는다()
    {
        var storage = new PlayerPrefsEntitlementStorage();

        storage.SetOwned(null, true);
        storage.SetOwned(string.Empty, true);

        Assert.IsFalse(storage.IsOwned(null));
        Assert.IsFalse(storage.IsOwned(string.Empty));
    }
}
