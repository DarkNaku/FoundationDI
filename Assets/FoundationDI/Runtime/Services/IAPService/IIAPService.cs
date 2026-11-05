using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 게임 코드가 보는 유일한 표면. 스토어별 차이(구글은 로컬 영수증 검증, 애플은 StoreKit 2),
    // Unity IAP의 Order 모델, 미확정 구매의 재전달은 전부 이 뒤에 가둔다.
    public interface IIAPService : IDisposable
    {
        bool IsInitialized { get; }
        Awaitable<bool> InitializeAsync();

        // 스토어에서 조회된 상품들. 초기화 전에는 비어 있다.
        IReadOnlyList<IAPProduct> Products { get; }

        bool TryGetProduct(string productId, out IAPProduct product);

        // 비소모성 소유 여부. 로컬 캐시를 보므로 오프라인에서도 답한다.
        // 캐시는 초기화·복원 때 스토어의 확정 목록으로 맞춰진다(재설치 후 복구, 환불 시 회수).
        bool IsOwned(string productId);

        Awaitable<IAPPurchaseResult> PurchaseAsync(string productId);

        // 미확정 구매를 지금 처리한다 — 지급 처리기 준비 전에 보류한 구매를 지급하고, 스토어에 미확정 구매를
        // 다시 물어 지급에 실패했던 구매를 재시도한다. 앱 재시작을 기다리지 않게 하는 경로다.
        // 진행 데이터 준비가 끝났을 때, 상점을 열 때, 포그라운드로 돌아올 때 부른다. 초기화 전에는 아무것도
        // 하지 않는다. 조회 결과가 오면 끝나며, 지급 결과는 Purchased 이벤트로 받는다.
        Awaitable ProcessUnconfirmedAsync();

        // iOS 심사가 요구하는 "구매 복원" 버튼의 구현. 비소모성 소유를 되살린다.
        Awaitable<IAPRestoreResult> RestoreAsync();

        // 검증·지급·확정이 모두 끝난 구매만 발행된다. 분석 연동 지점이다.
        //   _iap.Purchased += p => _analytics.LogPurchase(new PurchaseInfo(p.ProductId, p.Price, p.CurrencyCode));
        event Action<IAPPurchase> Purchased;

        // 비소모성 소유 상태가 바뀌었다(신규 구매, 복원, 환불로 인한 회수). 인자는 상품 ID.
        // 회수도 같은 이벤트로 오므로 값은 IsOwned로 다시 읽는다.
        event Action<string> OwnedChanged;
    }
}
