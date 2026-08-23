using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 게임 코드가 보는 유일한 표면. 스토어별 차이(구글은 로컬 영수증 검증, 애플은 StoreKit 2),
    // Unity IAP의 Order 모델, 미확정 구매의 재전달은 전부 이 뒤에 가둔다.
    public interface IIapService : IDisposable
    {
        bool IsInitialized { get; }
        Awaitable<bool> InitializeAsync();

        // 스토어에서 조회된 상품들. 초기화 전에는 비어 있다.
        IReadOnlyList<IapProduct> Products { get; }

        bool TryGetProduct(string productId, out IapProduct product);

        // 비소모성 소유 여부. 로컬 캐시를 보므로 오프라인에서도 답한다.
        bool IsOwned(string productId);

        Awaitable<IapPurchaseResult> PurchaseAsync(string productId);

        // iOS 심사가 요구하는 "구매 복원" 버튼의 구현. 비소모성 소유를 되살린다.
        Awaitable<IapRestoreResult> RestoreAsync();

        // 검증·지급·확정이 모두 끝난 구매만 발행된다. 분석 연동 지점이다.
        //   _iap.Purchased += p => _analytics.LogPurchase(new PurchaseInfo(p.ProductId, p.Price, p.CurrencyCode));
        event Action<IapPurchase> Purchased;

        // 비소모성 소유 상태가 바뀌었다(신규 구매 또는 복원). 인자는 상품 ID.
        event Action<string> OwnedChanged;
    }
}
