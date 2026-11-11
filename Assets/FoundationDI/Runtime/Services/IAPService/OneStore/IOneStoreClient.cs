using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 원스토어 SDK seam. OneStoreIAPService는 이것만 보고, SDK와 맞닿은 번역은 OneStorePurchaseClient가 맡는다.
    //
    // SDK의 IPurchaseCallback은 구매 조회와 구매 흐름의 결과를 같은 OnPurchaseSucceeded/OnPurchaseFailed로 보내고
    // 소비·승인 실패에는 어느 구매인지가 실려 오지 않는다. 그래서 요청마다 완료 콜백을 받는 모양으로 바꾼다 —
    // 구현은 요청을 하나씩 처리해 결과가 어느 요청의 것인지 섞이지 않게 한다. 콜백은 메인 스레드에서 부른다.
    internal interface IOneStoreClient : IDisposable
    {
        // 연결은 첫 요청이 연다. 연결 실패·로그인 실패도 그 요청의 실패로 온다.
        void QueryProducts(IReadOnlyList<string> productIds, Action<OneStoreResult, IReadOnlyList<OneStoreProduct>> onDone);

        // 소비하지 않은 관리형 상품 구매(승인한 비소모성 + 미확정 구매).
        void QueryPurchases(Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone);

        void Purchase(string productId, Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone);

        // 소모성의 확정. 소비하면 구매 조회에서 빠지고 다시 살 수 있다.
        void Consume(OneStorePurchase purchase, Action<OneStoreResult> onDone);

        // 비소모성의 확정. 승인하지 않은 구매는 3일 뒤 자동 환불된다.
        void Acknowledge(OneStorePurchase purchase, Action<OneStoreResult> onDone);

        // 요청 없이 도착한 구매(진행 중인 요청이 없을 때 온 구매 갱신).
        event Action<IReadOnlyList<OneStorePurchase>> PurchasesUpdated;
    }
}
