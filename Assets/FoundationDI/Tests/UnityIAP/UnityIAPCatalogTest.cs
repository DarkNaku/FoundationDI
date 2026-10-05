using System.Linq;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.TestTools;

public class UnityIAPCatalogTest
{
    private static ProductCatalog Catalog(params ProductCatalogItem[] items)
    {
        var catalog = new ProductCatalog();
        foreach (var item in items) catalog.Add(item);
        return catalog;
    }

    private static ProductCatalogItem Item(string id, ProductType type)
    {
        return new ProductCatalogItem { id = id, type = type };
    }

    [Test]
    public void 카탈로그를_공용_ID와_플랫폼_스토어_ID로_조회한다()
    {
        var removeAds = Item("remove_ads", ProductType.NonConsumable);
        removeAds.SetStoreID(GooglePlay.Name, "remove_ads_android");
        var catalog = Catalog(removeAds, Item("gems", ProductType.Consumable));

        var google = UnityIAPCatalog.ToDefinitions(catalog, GooglePlay.Name);
        var apple = UnityIAPCatalog.ToDefinitions(catalog, AppleAppStore.Name);

        var googleRemoveAds = google.Single(d => d.id == "remove_ads");
        Assert.AreEqual("remove_ads_android", googleRemoveAds.storeSpecificId, "스토어 오버라이드를 쓰지 않았다");
        Assert.AreEqual(ProductType.NonConsumable, googleRemoveAds.type);

        Assert.AreEqual("remove_ads", apple.Single(d => d.id == "remove_ads").storeSpecificId,
                        "오버라이드가 없는 스토어는 공용 ID로 폴백해야 한다");
        Assert.AreEqual("gems", google.Single(d => d.id == "gems").storeSpecificId);
    }

    [Test]
    public void 구독_상품은_경고_후_카탈로그에서_뺀다()
    {
        var catalog = Catalog(Item("vip", ProductType.Subscription), Item("gems", ProductType.Consumable));

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*vip"));

        var definitions = UnityIAPCatalog.ToDefinitions(catalog, GooglePlay.Name);

        CollectionAssert.AreEqual(new[] { "gems" }, definitions.Select(d => d.id).ToArray());
    }

    [Test]
    public void Codeless_자동_초기화가_켜진_카탈로그면_에러를_남긴다()
    {
        var catalog = Catalog(Item("gems", ProductType.Consumable));
        catalog.enableCodelessAutoInitialization = true;

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*Codeless"));

        UnityIAPCatalog.WarnIfCodelessAutoInitialization(catalog);

        // 끄면 조용하다.
        catalog.enableCodelessAutoInitialization = false;
        UnityIAPCatalog.WarnIfCodelessAutoInitialization(catalog);
        LogAssert.NoUnexpectedReceived();
    }
}
