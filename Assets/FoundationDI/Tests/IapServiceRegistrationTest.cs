using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class IapServiceRegistrationTest
{
    [TearDown]
    public void TearDown() => IapProviderRegistry.Reset();

    [Test]
    public void RegisterIapService로_등록하면_IIapService가_싱글턴으로_해석된다()
    {
        var settings = ScriptableObject.CreateInstance<IapServiceSettings>();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterIapService(settings);

            var container = builder.Build();

            var first = container.Resolve<IIapService>();
            var second = container.Resolve<IIapService>();

            Assert.IsNotNull(first);
            Assert.IsInstanceOf<IapService>(first);
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
        var settings = ScriptableObject.CreateInstance<IapServiceSettings>();

        try
        {
            var fulfillment = new FakeFulfillment();

            var builder = new ContainerBuilder();
            builder.RegisterInstance<IIapFulfillment>(fulfillment);
            builder.RegisterIapService(settings);

            var container = builder.Build();

            Assert.AreSame(fulfillment, container.Resolve<IIapFulfillment>());
            Assert.IsNotNull(container.Resolve<IIapService>());

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
        LogAssert.Expect(LogType.Error, new Regex("IapServiceSettings"));

        var builder = new ContainerBuilder();
        builder.RegisterIapService(null);

        var container = builder.Build();

        Assert.Throws<VContainerException>(() => container.Resolve<IIapService>());

        container.Dispose();
    }

    [Test]
    public void 설정의_상품_목록이_옵션으로_변환된다()
    {
        var settings = ScriptableObject.CreateInstance<IapServiceSettings>();

        try
        {
            settings.SetProductsForTest(new[]
            {
                new IapProductEntry("gems", IapProductType.Consumable, default),
                new IapProductEntry("remove_ads", IapProductType.NonConsumable, default),
            });

            var options = settings.ToOptions();

            Assert.AreEqual(2, options.Products.Count);
            Assert.AreEqual("gems", options.Products[0].Id);
            Assert.AreEqual("gems", options.Products[0].StoreId, "오버라이드가 비면 공용 ID를 그대로 써야 한다");
            Assert.AreEqual(IapProductType.NonConsumable, options.Products[1].Type);
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 빈_ID와_중복_ID는_경고와_함께_걸러진다()
    {
        var settings = ScriptableObject.CreateInstance<IapServiceSettings>();

        try
        {
            settings.SetProductsForTest(new[]
            {
                new IapProductEntry("gems", IapProductType.Consumable, default),
                new IapProductEntry("", IapProductType.Consumable, default),
                new IapProductEntry("gems", IapProductType.NonConsumable, default),
            });

            LogAssert.Expect(LogType.Warning, new Regex("IAPService"));
            LogAssert.Expect(LogType.Warning, new Regex("IAPService"));

            var options = settings.ToOptions();

            Assert.AreEqual(1, options.Products.Count);
            Assert.AreEqual(IapProductType.Consumable, options.Products[0].Type, "먼저 온 항목이 이겨야 한다");
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }
}
