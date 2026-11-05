using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine.Purchasing;

public class IAPServiceRegistrationTest
{
    private System.Func<IAPServiceDependencies, IIAPService> _original;

    private IIAPFulfillment _fulfillment;
    private IReceiptValidator _validator;
    private IEntitlementStorage _entitlements;
    private bool _verboseLogging;
    private IAPServiceDependencies _dependencies;
    private int _created;

    [SetUp]
    public void SetUp()
    {
        _original = IAPServiceRegistration.ServiceFactory;
        _created = 0;

        // 에디트 모드에서는 StoreController를 만들 수 없다 — 배선만 검증하고 서비스는 대체 스토어로 만든다.
        IAPServiceRegistration.ServiceFactory = dependencies =>
        {
            _created++;
            _dependencies = dependencies;
            _fulfillment = dependencies.Fulfillment;
            _validator = dependencies.Validator;
            _entitlements = dependencies.Entitlements;
            _verboseLogging = dependencies.Options.VerboseLogging;

            var store = new UnityIAPStoreHarness();
            return new IAPService(store.Store, store.Products, store.Purchases, new ProductDefinition[0],
                                  dependencies);
        };
    }

    [TearDown]
    public void TearDown() => IAPServiceRegistration.ServiceFactory = _original;

    [Test]
    public void RegisterIAPService로_등록하면_IIAPService가_싱글턴으로_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterIAPService();

        var container = builder.Build();

        var first = container.Resolve<IIAPService>();
        var second = container.Resolve<IIAPService>();

        Assert.IsInstanceOf<IAPService>(first);
        Assert.AreSame(first, second, "싱글턴으로 등록되지 않았다");
        Assert.AreEqual(1, _created);

        Assert.DoesNotThrow(() => container.Dispose());
    }

    [Test]
    public void 선택_seam을_등록하지_않으면_기본_구현으로_폴백한다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterIAPService();

        builder.Build().Resolve<IIAPService>();

        Assert.IsInstanceOf<AutoConfirmFulfillment>(_fulfillment);
        Assert.IsInstanceOf<CrossPlatformReceiptValidator>(_validator);
        Assert.IsInstanceOf<PlayerPrefsEntitlementStorage>(_entitlements);
        Assert.IsFalse(_verboseLogging);
    }

    [Test]
    public void 게임이_등록한_seam과_로그_설정이_전달된다()
    {
        var fulfillment = new FakeFulfillment();
        var validator = new FakeReceiptValidator();
        var entitlements = new FakeEntitlementStorage();

        var builder = new ContainerBuilder();
        builder.RegisterValue(fulfillment, new[] { typeof(IIAPFulfillment) });
        builder.RegisterValue(validator, new[] { typeof(IReceiptValidator) });
        builder.RegisterValue(entitlements, new[] { typeof(IEntitlementStorage) });
        builder.RegisterIAPService(verboseLogging: true);

        builder.Build().Resolve<IIAPService>();

        Assert.AreSame(fulfillment, _fulfillment);
        Assert.AreSame(validator, _validator);
        Assert.AreSame(entitlements, _entitlements);
        Assert.IsTrue(_verboseLogging);
    }

    [Test]
    public void 서버_검증기와_옵션이_전달되고_없으면_서버_검증을_하지_않는다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterIAPService();
        builder.Build().Resolve<IIAPService>();

        Assert.IsNull(_dependencies.AsyncValidator);
        Assert.IsNotNull(_dependencies.Dispatcher, "시간 제한용 디스패처를 넘기지 않았다");

        var asyncValidator = new FakeAsyncReceiptValidator();
        var options = new IAPServiceOptions { PurchaseTimeoutSeconds = 10f };

        builder = new ContainerBuilder();
        builder.RegisterValue(asyncValidator, new[] { typeof(IAsyncReceiptValidator) });
        builder.RegisterIAPService(options);
        builder.Build().Resolve<IIAPService>();

        Assert.AreSame(asyncValidator, _dependencies.AsyncValidator);
        Assert.AreSame(options, _dependencies.Options);
    }
}
