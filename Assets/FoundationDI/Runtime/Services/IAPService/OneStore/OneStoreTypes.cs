namespace DarkNaku.FoundationDI
{
    // 원스토어 SDK의 ResponseCode 중 이 서비스가 구분하는 값.
    internal static class OneStoreResponseCodes
    {
        public const int Ok = 0;
        public const int UserCanceled = 1;
        public const int ItemAlreadyOwned = 7;
        public const int NeedLogin = 10;
        public const int NeedUpdate = 11;

        // SDK 코드가 아니다. 이 서비스의 클라이언트가 정한 값이다.
        public const int Unsupported = -2101;   // 안드로이드가 아니다
        public const int NoResponse = -2102;    // SDK가 응답하지 않았다
        public const int Disposed = -2103;
    }

    // 원스토어 PurchaseData.PurchaseState.
    internal static class OneStorePurchaseStates
    {
        public const int Purchased = 0;
    }

    internal readonly struct OneStoreResult
    {
        public int Code { get; }
        public string Message { get; }

        public OneStoreResult(int code, string message)
        {
            Code = code;
            Message = message;
        }

        public bool IsSuccess => Code == OneStoreResponseCodes.Ok;

        public static OneStoreResult Ok => new(OneStoreResponseCodes.Ok, null);

        public override string ToString() => $"({Code}) {Message}";
    }

    // 원스토어 ProductDetail. Price는 통화 기호 없는 숫자 문자열이다(예: "1200").
    internal sealed class OneStoreProduct
    {
        public string ProductId { get; }
        public string Title { get; }
        public string Price { get; }
        public string PriceCurrencyCode { get; }
        public long PriceAmountMicros { get; }

        public OneStoreProduct(string productId, string title, string price, string priceCurrencyCode,
                               long priceAmountMicros)
        {
            ProductId = productId;
            Title = title;
            Price = price;
            PriceCurrencyCode = priceCurrencyCode;
            PriceAmountMicros = priceAmountMicros;
        }
    }

    // 원스토어 PurchaseData. Source는 클라이언트가 소비·승인에 다시 쓰는 SDK 객체다.
    internal sealed class OneStorePurchase
    {
        public string ProductId { get; }
        public string OrderId { get; }
        public string PurchaseToken { get; }
        public int PurchaseState { get; }
        public bool IsAcknowledged { get; }
        public string Receipt { get; }
        public object Source { get; }

        public OneStorePurchase(string productId, string orderId, string purchaseToken, int purchaseState,
                                bool isAcknowledged, string receipt, object source = null)
        {
            ProductId = productId;
            OrderId = orderId;
            PurchaseToken = purchaseToken;
            PurchaseState = purchaseState;
            IsAcknowledged = isAcknowledged;
            Receipt = receipt;
            Source = source;
        }

        // 주문 ID가 트랜잭션 ID다. 비어 있으면 구매 토큰으로 대신한다.
        public string TransactionId => string.IsNullOrEmpty(OrderId) ? PurchaseToken : OrderId;
    }
}
