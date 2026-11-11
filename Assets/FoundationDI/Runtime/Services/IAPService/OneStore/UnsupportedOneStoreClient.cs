using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 안드로이드가 아닌 곳(에디터 포함)의 클라이언트. 원스토어 SDK는 안드로이드에서만 동작한다 —
    // 모든 요청을 곧바로 실패시켜 초기화가 false로 끝나게 한다(게임은 IAP 없이 계속 돈다).
    internal sealed class UnsupportedOneStoreClient : IOneStoreClient
    {
        private static readonly OneStoreResult Unsupported =
            new(OneStoreResponseCodes.Unsupported, "원스토어 결제는 안드로이드 기기에서만 동작한다");

        public event Action<IReadOnlyList<OneStorePurchase>> PurchasesUpdated { add { } remove { } }

        public void QueryProducts(IReadOnlyList<string> productIds,
                                  Action<OneStoreResult, IReadOnlyList<OneStoreProduct>> onDone) =>
            onDone?.Invoke(Unsupported, Array.Empty<OneStoreProduct>());

        public void QueryPurchases(Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone) =>
            onDone?.Invoke(Unsupported, Array.Empty<OneStorePurchase>());

        public void Purchase(string productId, Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone) =>
            onDone?.Invoke(Unsupported, Array.Empty<OneStorePurchase>());

        public void Consume(OneStorePurchase purchase, Action<OneStoreResult> onDone) => onDone?.Invoke(Unsupported);

        public void Acknowledge(OneStorePurchase purchase, Action<OneStoreResult> onDone) => onDone?.Invoke(Unsupported);

        public void Dispose()
        {
        }
    }
}
