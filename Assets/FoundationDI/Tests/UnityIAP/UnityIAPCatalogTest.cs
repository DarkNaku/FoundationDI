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

    [Test]
    public void 코드로_넘긴_상품을_스토어별_ID와_Unity_타입으로_바꾼다()
    {
        var products = new[]
        {
            new IAPProductDefinition("gems_100", IAPProductType.Consumable, googlePlayId: "gp_gems_100", appleId: "ios_gems_100"),
            new IAPProductDefinition("remove_ads", IAPProductType.NonConsumable),
        };

        var google = UnityIAPCatalog.ToDefinitions(products, GooglePlay.Name);
        var apple = UnityIAPCatalog.ToDefinitions(products, AppleAppStore.Name);

        Assert.AreEqual("gp_gems_100", google.Single(d => d.id == "gems_100").storeSpecificId);
        Assert.AreEqual("ios_gems_100", apple.Single(d => d.id == "gems_100").storeSpecificId);
        Assert.AreEqual(ProductType.Consumable, google.Single(d => d.id == "gems_100").type);
        Assert.AreEqual("remove_ads", google.Single(d => d.id == "remove_ads").storeSpecificId,
                        "스토어별 ID가 비면 공용 ID를 쓴다");
        Assert.AreEqual(ProductType.NonConsumable, apple.Single(d => d.id == "remove_ads").type);
    }

    [Test]
    public void 코드로_넘긴_상품의_중복_ID와_빈_ID는_경고하고_뺀다()
    {
        var products = new[]
        {
            new IAPProductDefinition("gems", IAPProductType.Consumable),
            new IAPProductDefinition("gems", IAPProductType.Consumable, googlePlayId: "other"),
            new IAPProductDefinition("", IAPProductType.Consumable),
        };

        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*gems"));
        LogAssert.Expect(LogType.Warning, new Regex("IAPService.*빈"));

        var definitions = UnityIAPCatalog.ToDefinitions(products, GooglePlay.Name);

        Assert.AreEqual(1, definitions.Count);
        Assert.AreEqual("gems", definitions[0].storeSpecificId, "먼저 나온 정의를 쓴다");
    }

    [Test]
    public void 옵션에_상품_목록을_넘기면_IAP_Catalog_대신_쓴다()
    {
        var options = new IAPServiceOptions
        {
            CatalogSource = () => new[] { new IAPProductDefinition("from_code", IAPProductType.Consumable) },
        };

        var definitions = UnityIAPCatalog.Resolve(options, GooglePlay.Name);

        CollectionAssert.AreEqual(new[] { "from_code" }, definitions.Select(d => d.id).ToArray());
    }

    [Test]
    public void 상품_목록_공급자가_예외를_던지면_에러를_남기고_빈_카탈로그로_둔다()
    {
        var options = new IAPServiceOptions
        {
            CatalogSource = () => throw new System.InvalidOperationException("표를 읽지 못했다"),
        };

        LogAssert.Expect(LogType.Error, new Regex("IAPService.*표를 읽지 못했다"));

        Assert.IsEmpty(UnityIAPCatalog.Resolve(options, GooglePlay.Name));
    }
}
