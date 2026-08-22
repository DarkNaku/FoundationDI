using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // SDK seam. Unity IAP든 다른 무엇이든 이 인터페이스만 만족하면 정책 계층은 바뀌지 않는다.
    //
    // 설계상 중요한 제약이 하나 있다: provider는 구매를 스스로 확정하지 않는다.
    // 지급이 저장된 뒤에만 확정해야 재화 유실이 없기 때문에, 확정 시점은 전적으로 정책 계층이 정한다.
    public interface IIapProvider : IDisposable
    {
        string Name { get; }

        // 스토어에 연결하고 카탈로그를 조회한다. 미확정 구매가 있으면 이 안에서
        // PurchasePending으로 재발행한다 — 그래서 정책 계층은 이 호출 전에 구독을 끝내야 한다.
        Awaitable<bool> InitializeAsync(IapProviderContext context);

        IReadOnlyList<IapProduct> Products { get; }

        // 구매를 시작만 한다. 결과는 PurchasePending/PurchaseFailed/PurchaseDeferred로 온다.
        // false는 "시작조차 못 했다"는 뜻이며 이때는 어떤 이벤트도 오지 않는다.
        bool Purchase(string storeId);

        void Confirm(string transactionId);

        Awaitable<bool> RestoreAsync();

        event Action<IapPendingPurchase> PurchasePending;
        event Action<IapPurchaseFailure> PurchaseFailed;
        event Action<string> PurchaseDeferred;   // storeId
    }
}
