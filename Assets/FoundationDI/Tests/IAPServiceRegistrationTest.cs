using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Reflex.Core;

public class IAPServiceRegistrationTest
{
    [TearDown]
    public void TearDown() => IAPProviderRegistry.Reset();

    [Test]
    public void RegisterIAPService로_등록하면_IIAPService가_싱글턴으로_해석된다()
    {
        var settings = ScriptableObject.CreateInstance<IAPServiceSettings>();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterIAPService(settings);

            var container = builder.Build();

            var first = container.Resolve<IIAPService>();
            var second = container.Resolve<IIAPService>();

            Assert.IsNotNull(first);
            Assert.IsInstanceOf<IAPService>(first);
            Assert.AreSame(first, second, "싱글턴으로 등록되지 않았다");

            Assert.DoesNotThrow(() => container.Dispose());
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 게임이_등록한_지급_핸들러가_주입된다()
    {
        var settings = ScriptableObject.CreateInstance<IAPServiceSettings>();

        try
        {
            var fulfillment = new FakeFulfillment();

            var builder = new ContainerBuilder();
            builder.RegisterValue(fulfillment, new[] { typeof(IIAPFulfillment) });
            builder.RegisterIAPService(settings);

            var container = builder.Build();

            Assert.AreSame(fulfillment, container.Resolve<IIAPFulfillment>());
            Assert.IsNotNull(container.Resolve<IIAPService>());

            container.Dispose();
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void settings가_null이면_에러_로그만_남기고_등록하지_않는다()
    {
        LogAssert.Expect(LogType.Error, new Regex("IAPServiceSettings"));

        var builder = new ContainerBuilder();
        builder.RegisterIAPService(null);

        var container = builder.Build();

        Assert.Throws<Reflex.Exceptions.UnknownContractException>(
            () => container.Resolve<IIAPService>());

        container.Dispose();
    }

    [Test]
    public void 설정의_상품_목록이_옵션으로_변환된다()
    {
        var settings = ScriptableObject.CreateInstance<IAPServiceSettings>();

        try
        {
            settings.SetProductsForTest(new[]
            {
                new IAPProductEntry("gems", IAPProductType.Consumable, default),
                new IAPProductEntry("remove_ads", IAPProductType.NonConsumable, default),
            });

            var options = settings.ToOptions();

            Assert.AreEqual(2, options.Products.Count);
            Assert.AreEqual("gems", options.Products[0].Id);
            Assert.AreEqual("gems", options.Products[0].StoreId, "오버라이드가 비면 공용 ID를 그대로 써야 한다");
            Assert.AreEqual(IAPProductType.NonConsumable, options.Products[1].Type);
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 빈_ID와_중복_ID는_경고와_함께_걸러진다()
    {
        var settings = ScriptableObject.CreateInstance<IAPServiceSettings>();

        try
        {
            settings.SetProductsForTest(new[]
            {
                new IAPProductEntry("gems", IAPProductType.Consumable, default),
                new IAPProductEntry("", IAPProductType.Consumable, default),
                new IAPProductEntry("gems", IAPProductType.NonConsumable, default),
            });

            LogAssert.Expect(LogType.Warning, new Regex("IAPService"));
            LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

            var options = settings.ToOptions();

            Assert.AreEqual(1, options.Products.Count);
            Assert.AreEqual(IAPProductType.Consumable, options.Products[0].Type, "먼저 온 항목이 이겨야 한다");
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }
}
