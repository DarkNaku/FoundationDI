using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine.Purchasing;

public class OneStoreIAPServiceRegistrationTest
{
    private System.Func<string, IAPServiceDependencies, IIAPService> _original;

    private string _licenseKey;
    private IAPServiceDependencies _dependencies;
    private int _created;

    [SetUp]
    public void SetUp()
    {
        _original = OneStoreIAPServiceRegistration.ServiceFactory;
        _created = 0;

        // 에디트 모드에는 원스토어 SDK가 없다 — 배선만 검증하고 서비스는 대역 클라이언트로 만든다.
        OneStoreIAPServiceRegistration.ServiceFactory = (licenseKey, dependencies) =>
        {
            _created++;
            _licenseKey = licenseKey;
            _dependencies = dependencies;

            return new OneStoreIAPService(new FakeOneStoreClient(), new ProductDefinition[0], dependencies);
        };
    }

    [TearDown]
    public void TearDown() => OneStoreIAPServiceRegistration.ServiceFactory = _original;

    [Test]
    public void 등록하면_IIAPService가_싱글턴으로_해석되고_라이선스_키가_전달된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterOneStoreIAPService("license-key");

        var container = builder.Build();

        var first = container.Resolve<IIAPService>();
        var second = container.Resolve<IIAPService>();

        Assert.IsInstanceOf<OneStoreIAPService>(first);
        Assert.AreSame(first, second, "싱글턴으로 등록되지 않았다");
        Assert.AreEqual(1, _created);
        Assert.AreEqual("license-key", _licenseKey);

        Assert.DoesNotThrow(() => container.Dispose());
    }

    [Test]
    public void 검증기를_등록하지_않으면_원스토어_검증기를_쓴다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterOneStoreIAPService("license-key");

        builder.Build().Resolve<IIAPService>();

        // 구글 Tangle 검증기는 원스토어 영수증을 통과시키지 못한다.
        Assert.IsInstanceOf<OneStoreReceiptValidator>(_dependencies.Validator);
        Assert.IsInstanceOf<AutoConfirmFulfillment>(_dependencies.Fulfillment);
        Assert.IsNull(_dependencies.AsyncValidator);
    }

    [Test]
    public void 게임이_등록한_지급_처리기와_검증기를_쓴다()
    {
        var fulfillment = new FakeFulfillment();
        var validator = new FakeReceiptValidator();

        var builder = new ContainerBuilder();
        builder.RegisterValue(fulfillment, new[] { typeof(IIAPFulfillment) });
        builder.RegisterValue(validator, new[] { typeof(IReceiptValidator) });
        builder.RegisterOneStoreIAPService("license-key");

        builder.Build().Resolve<IIAPService>();

        Assert.AreSame(fulfillment, _dependencies.Fulfillment);
        Assert.AreSame(validator, _dependencies.Validator);
    }

    [Test]
    public void 코드로_넘긴_상품은_원스토어_ID를_쓰고_비면_공용_ID를_쓴다()
    {
        var definitions = UnityIAPCatalog.ToDefinitions(new[]
        {
            new IAPProductDefinition("gems", IAPProductType.Consumable, googlePlayId: "gems_gp", oneStoreId: "gems_os"),
            new IAPProductDefinition("remove_ads", IAPProductType.NonConsumable, googlePlayId: "remove_ads_gp"),
        }, OneStoreIAPService.StoreName);

        Assert.AreEqual("gems_os", definitions[0].storeSpecificId);
        Assert.AreEqual("remove_ads", definitions[1].storeSpecificId);
    }
}
