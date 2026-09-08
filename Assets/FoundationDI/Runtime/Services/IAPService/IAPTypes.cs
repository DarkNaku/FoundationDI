using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 구독은 범위 밖이다. 스토어가 소모성/비소모성을 다르게 취급하고(재구매 가능 여부,
    // 복원 대상 여부) 정책 계층의 분기도 이 둘로 갈린다.
    public enum IAPProductType { Consumable, NonConsumable }

    public enum IAPPurchaseOutcome
    {
        Purchased,       // 신규 구매 — 검증·지급·확정까지 끝났다
        Restored,        // 복원 또는 재전달된 미확정 구매
        AlreadyOwned,    // 비소모성인데 이미 소유 — 스토어를 거치지 않았다
        UserCancelled,   // 사용자가 스토어 시트를 닫았다. 에러가 아니다
        Deferred,        // iOS Ask-to-Buy 등 — 나중에 Purchased 이벤트로 온다
        NotReady,        // 초기화 안 됨 / 카탈로그에 없음 / 중복 호출
        InvalidReceipt,  // 영수증 검증 실패 — 지급도 확정도 하지 않았다
        Failed,          // 그 외 스토어 실패 및 지급 실패
    }

    public readonly struct IAPError
    {
        public int Code { get; }
        public string Message { get; }

        public IAPError(int code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString() => $"({Code}) {Message}";
    }

    // 게임이 쓰는 공용 ID와 실제 스토어 ID를 짝지은 카탈로그 항목. 플랫폼 해석은 이미 끝난 상태다.
    public readonly struct IAPProductDefinition
    {
        public string Id { get; }
        public string StoreId { get; }
        public IAPProductType Type { get; }

        public IAPProductDefinition(string id, string storeId, IAPProductType type)
        {
            Id = id;
            StoreId = storeId;
            Type = type;
        }
    }

    // 스토어에서 조회된 상품. LocalizedPrice는 그대로 UI에 찍고, Price/CurrencyCode는 분석 전송용이다.
    public readonly struct IAPProduct
    {
        public string Id { get; }
        public string StoreId { get; }
        public IAPProductType Type { get; }
        public string Title { get; }
        public string Description { get; }
        public string LocalizedPrice { get; }
        public double Price { get; }
        public string CurrencyCode { get; }
        public bool IsAvailable { get; }

        public IAPProduct(string id, string storeId, IAPProductType type, string title, string description,
                          string localizedPrice, double price, string currencyCode, bool isAvailable)
        {
            Id = id;
            StoreId = storeId;
            Type = type;
            Title = title;
            Description = description;
            LocalizedPrice = localizedPrice;
            Price = price;
            CurrencyCode = currencyCode;
            IsAvailable = isAvailable;
        }
    }

    // 확정된 구매. AnalyticsService의 LogPurchase에 이벤트 핸들러로 직접 대입할 수 있어야 하므로
    // 이 타입을 받는 API에 in 한정자를 쓰지 않는다(in이 붙으면 Action<T>에 대입할 수 없다).
    public readonly struct IAPPurchase
    {
        public string ProductId { get; }
        public IAPProductType Type { get; }
        public string TransactionId { get; }
        public string Receipt { get; }
        public double Price { get; }
        public string CurrencyCode { get; }
        public bool IsRestored { get; }

        public IAPPurchase(string productId, IAPProductType type, string transactionId, string receipt,
                           double price, string currencyCode, bool isRestored)
        {
            ProductId = productId;
            Type = type;
            TransactionId = transactionId;
            Receipt = receipt;
            Price = price;
            CurrencyCode = currencyCode;
            IsRestored = isRestored;
        }
    }

    public readonly struct IAPPurchaseResult
    {
        public IAPPurchaseOutcome Outcome { get; }
        public IAPPurchase Purchase { get; }   // Purchased/Restored/AlreadyOwned 일 때만 유효
        public IAPError Error { get; }         // InvalidReceipt/Failed 일 때만 유효

        private IAPPurchaseResult(IAPPurchaseOutcome outcome, IAPPurchase purchase, IAPError error)
        {
            Outcome = outcome;
            Purchase = purchase;
            Error = error;
        }

        public static IAPPurchaseResult Purchased(IAPPurchase purchase) =>
            new(IAPPurchaseOutcome.Purchased, purchase, default);

        public static IAPPurchaseResult Restored(IAPPurchase purchase) =>
            new(IAPPurchaseOutcome.Restored, purchase, default);

        public static IAPPurchaseResult AlreadyOwned(IAPPurchase purchase) =>
            new(IAPPurchaseOutcome.AlreadyOwned, purchase, default);

        public static IAPPurchaseResult UserCancelled() =>
            new(IAPPurchaseOutcome.UserCancelled, default, default);

        public static IAPPurchaseResult Deferred() =>
            new(IAPPurchaseOutcome.Deferred, default, default);

        public static IAPPurchaseResult NotReady() =>
            new(IAPPurchaseOutcome.NotReady, default, default);

        public static IAPPurchaseResult InvalidReceipt(IAPError error) =>
            new(IAPPurchaseOutcome.InvalidReceipt, default, error);

        public static IAPPurchaseResult Failed(IAPError error) =>
            new(IAPPurchaseOutcome.Failed, default, error);

        // 소유권을 확보한 결과. 신규 구매인지 복원인지 이미 갖고 있었는지는 구분하지 않는다 —
        // 호출부는 대부분 "줘도 되는가"만 알면 된다.
        public bool IsSuccess => Outcome is IAPPurchaseOutcome.Purchased
                                         or IAPPurchaseOutcome.Restored
                                         or IAPPurchaseOutcome.AlreadyOwned;
    }

    public readonly struct IAPRestoreResult
    {
        public bool Success { get; }
        public int RestoredCount { get; }
        public IAPError Error { get; }

        private IAPRestoreResult(bool success, int restoredCount, IAPError error)
        {
            Success = success;
            RestoredCount = restoredCount;
            Error = error;
        }

        public static IAPRestoreResult Ok(int restoredCount) => new(true, restoredCount, default);
        public static IAPRestoreResult Fail(IAPError error) => new(false, 0, error);
    }

    // provider → 정책 계층. 아직 확정되지 않은 구매다. 확정 시점은 provider가 아니라 정책 계층이 정한다.
    public readonly struct IAPPendingPurchase
    {
        public string StoreId { get; }
        public string TransactionId { get; }
        public string Receipt { get; }
        public bool IsRestored { get; }

        public IAPPendingPurchase(string storeId, string transactionId, string receipt, bool isRestored)
        {
            StoreId = storeId;
            TransactionId = transactionId;
            Receipt = receipt;
            IsRestored = isRestored;
        }
    }

    public readonly struct IAPPurchaseFailure
    {
        public string StoreId { get; }
        public bool IsUserCancelled { get; }
        public IAPError Error { get; }

        public IAPPurchaseFailure(string storeId, bool isUserCancelled, IAPError error)
        {
            StoreId = storeId;
            IsUserCancelled = isUserCancelled;
            Error = error;
        }
    }

    // provider가 초기화에 필요로 하는 것만 담는다. 설정 SO 전체를 넘기지 않는 이유는
    // 카탈로그 외의 값이 상위 계층 소관이라 provider가 볼 이유가 없기 때문이다.
    public readonly struct IAPProviderContext
    {
        public IReadOnlyList<IAPProductDefinition> Products { get; }
        public bool VerboseLogging { get; }

        public IAPProviderContext(IReadOnlyList<IAPProductDefinition> products, bool verboseLogging)
        {
            Products = products;
            VerboseLogging = verboseLogging;
        }
    }

    public readonly struct IAPServiceOptions
    {
        public IReadOnlyList<IAPProductDefinition> Products { get; }
        public bool VerboseLogging { get; }

        public IAPServiceOptions(IReadOnlyList<IAPProductDefinition> products, bool verboseLogging)
        {
            Products = products;
            VerboseLogging = verboseLogging;
        }
    }
}
