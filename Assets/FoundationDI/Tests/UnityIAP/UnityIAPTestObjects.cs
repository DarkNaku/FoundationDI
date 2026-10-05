using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Purchasing;

// Unity IAP 객체 생성 헬퍼. 주문·카트·영수증 정보는 public 생성자가 있지만 Product와
// ProductFetchFailed는 internal이다 — 리플렉션은 여기 한 곳에서만 쓴다. Unity IAP가 이 생성자를
// 바꾸면 이 헬퍼가 테스트에서 먼저 깨진다(프로덕션 코드는 영향이 없다).
public static class UnityIAPTestObjects
{
    public static ProductDefinition Definition(string id, ProductType type, string storeId = null) =>
        new(id, storeId ?? id, type);

    public static Product Product(ProductDefinition definition, string priceString = "$0.99",
                                  decimal price = 0.99m, string currency = "USD", bool available = true)
    {
        var metadata = new ProductMetadata(priceString, $"{definition.id} title", $"{definition.id} desc",
                                           currency, price);

        var ctor = typeof(Product).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            new[] { typeof(ProductDefinition), typeof(ProductMetadata), typeof(bool) }, null);

        if (ctor == null) throw new InvalidOperationException("Product(ProductDefinition, ProductMetadata, bool) 생성자를 찾지 못했다.");

        return (Product)ctor.Invoke(new object[] { definition, metadata, available });
    }

    public static Product Product(string id, ProductType type) => Product(Definition(id, type));

    public static PendingOrder Pending(Product product, string transactionId, string receipt = "receipt") =>
        new(new Cart(new CartItem(product)), new FakeOrderInfo(receipt, transactionId));

    public static ConfirmedOrder Confirmed(Product product) =>
        new(new Cart(new CartItem(product)), new FakeOrderInfo("receipt", string.Empty));

    public static DeferredOrder Deferred(Product product) =>
        new(new Cart(new CartItem(product)), new FakeOrderInfo("receipt", "deferred"));

    public static FailedOrder Failed(Product product, PurchaseFailureReason reason, string details = "failed") =>
        new(new Cart(new CartItem(product)), reason, details);

    public static Orders Orders(IEnumerable<ConfirmedOrder> confirmed = null, IEnumerable<PendingOrder> pending = null) =>
        new(new List<ConfirmedOrder>(confirmed ?? Array.Empty<ConfirmedOrder>()),
            new List<PendingOrder>(pending ?? Array.Empty<PendingOrder>()),
            new List<DeferredOrder>());

    public static ProductFetchFailed ProductFetchFailed(List<ProductDefinition> products, string reason)
    {
        var ctor = typeof(ProductFetchFailed).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            new[] { typeof(List<ProductDefinition>), typeof(string) }, null);

        if (ctor == null) throw new InvalidOperationException("ProductFetchFailed(List, string) 생성자를 찾지 못했다.");

        return (ProductFetchFailed)ctor.Invoke(new object[] { products, reason });
    }
}

// Unity의 OrderInfo는 internal이다. 주문 생성자가 받는 것은 public 인터페이스라 직접 구현한다.
public class FakeOrderInfo : IOrderInfo
{
    public FakeOrderInfo(string receipt, string transactionId)
    {
        Receipt = receipt;
        TransactionID = transactionId;
    }

    public IAppleOrderInfo Apple => null;
    public IGoogleOrderInfo Google => null;
    public IPaymentProvidersOrderInfo PaymentProviders => null;
    public List<IPurchasedProductInfo> PurchasedProductInfo { get; set; } = new();
    public string Receipt { get; }
    public string TransactionID { get; }
}
