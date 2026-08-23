using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityProduct = UnityEngine.Purchasing.Product;
using UnityProductType = UnityEngine.Purchasing.ProductType;

namespace DarkNaku.FoundationDI
{
    // Unity IAP 5.x 어댑터. 정책은 하나도 갖지 않는다 — 번역만 한다.
    //
    // v5는 v4의 IStoreListener/ProcessPurchase를 버리고 Order 모델로 갔다. 확정이 명시적
    // 호출(ConfirmPurchase)로 분리된 덕분에 "지급을 저장한 뒤에만 확정한다"는 규율을
    // 정책 계층이 그대로 구현할 수 있다.
    public sealed class UnityIapProvider : IIapProvider
    {
        private readonly StoreController _controller;
        private readonly List<IapProduct> _products = new();

        // 스토어 ID → 조회된 Product. PurchaseProduct(Product)로 사는 편이
        // catalogListingId 규칙에 의존하지 않아 안전하다.
        private readonly Dictionary<string, UnityProduct> _productsByStoreId = new();

        // 아직 확정하지 않은 주문. 정책 계층이 트랜잭션 ID로만 확정을 요청하기 때문에 여기서 되찾는다.
        private readonly Dictionary<string, PendingOrder> _unconfirmed = new();

        private AwaitableCompletionSource<bool> _connecting;
        private AwaitableCompletionSource<bool> _fetchingProducts;
        private AwaitableCompletionSource<bool> _fetchingPurchases;
        private AwaitableCompletionSource<bool> _restoring;

        private bool _verboseLogging;
        private bool _subscribed;
        private bool _disposed;

        public UnityIapProvider() : this(UnityIAPServices.StoreController())
        {
        }

        internal UnityIapProvider(StoreController controller)
        {
            _controller = controller;
        }

        public string Name => "UnityIAP";

        public IReadOnlyList<IapProduct> Products => _products;

        public event Action<IapPendingPurchase> PurchasePending;
        public event Action<IapPurchaseFailure> PurchaseFailed;
        public event Action<string> PurchaseDeferred;

        public async Awaitable<bool> InitializeAsync(IapProviderContext context)
        {
            _verboseLogging = context.VerboseLogging;

            // 미확정 주문을 SDK가 임의로 재처리하지 않게 한다. 확정 시점은 정책 계층 소관이고,
            // 재전달된 주문에는 "복원됨" 표시를 붙여야 하는데 자동 경로로는 그 구분이 사라진다.
            _controller.ProcessPendingOrdersOnPurchasesFetched(false);

            Subscribe();

            if (!await ConnectAsync()) return false;
            if (!await FetchProductsAsync(context)) return false;

            // 실패해도 치명적이지 않다 — 미확정 구매를 이번 실행에 못 찾을 뿐이고 다음 실행에 다시 시도한다.
            await FetchPurchasesAsync();

            return true;
        }

        public bool Purchase(string storeId)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(storeId) || !_productsByStoreId.TryGetValue(storeId, out var product)) return false;

            _controller.PurchaseProduct(product);
            return true;
        }

        public void Confirm(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId)) return;
            if (!_unconfirmed.Remove(transactionId, out var order)) return;

            _controller.ConfirmPurchase(order);
        }

        public Awaitable<bool> RestoreAsync()
        {
            if (_disposed) return Completed(false);

            _restoring = new AwaitableCompletionSource<bool>();

            // 복원으로 돌아온 주문은 OnPurchasePending으로 들어온다. 그때 IsRestored를 붙일 수
            // 있도록 콜백이 끝나기 전까지를 "복원 구간"으로 본다.
            _restoreInProgress = true;

            _controller.RestoreTransactions((success, error) =>
            {
                _restoreInProgress = false;

                if (!success) Debug.LogWarning($"[IAPService] 복원에 실패했다: {error}");

                _restoring?.TrySetResult(success);
            });

            return _restoring.Awaitable;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Unsubscribe();

            _unconfirmed.Clear();
            _productsByStoreId.Clear();
            _products.Clear();

            PurchasePending = null;
            PurchaseFailed = null;
            PurchaseDeferred = null;
        }

        private bool _restoreInProgress;

        private Awaitable<bool> ConnectAsync()
        {
            _connecting = new AwaitableCompletionSource<bool>();

            // Connect()가 돌려주는 Task를 직접 await 하지 않는다. 성공/실패가 모두
            // OnStoreConnected/OnStoreDisconnected로 오기 때문에 이벤트 하나로 받는 편이
            // 스레드 문맥에 대한 가정이 없다.
            _ = _controller.Connect();

            return _connecting.Awaitable;
        }

        private Awaitable<bool> FetchProductsAsync(IapProviderContext context)
        {
            var definitions = new List<ProductDefinition>();

            if (context.Products != null)
            {
                foreach (var definition in context.Products)
                {
                    if (string.IsNullOrEmpty(definition.StoreId)) continue;

                    definitions.Add(new ProductDefinition(definition.StoreId, definition.StoreId,
                                                          ToUnityType(definition.Type)));
                }
            }

            _catalog = context.Products;

            if (definitions.Count == 0)
            {
                Debug.LogWarning("[IAPService] 카탈로그가 비어 있다. 조회할 상품이 없다.");
                return Completed(true);
            }

            _fetchingProducts = new AwaitableCompletionSource<bool>();
            _controller.FetchProducts(definitions);
            return _fetchingProducts.Awaitable;
        }

        private Awaitable<bool> FetchPurchasesAsync()
        {
            _fetchingPurchases = new AwaitableCompletionSource<bool>();
            _controller.FetchPurchases();
            return _fetchingPurchases.Awaitable;
        }

        private IReadOnlyList<IapProductDefinition> _catalog;

        private void Subscribe()
        {
            if (_subscribed) return;

            _controller.OnStoreConnected += HandleStoreConnected;
            _controller.OnStoreDisconnected += HandleStoreDisconnected;
            _controller.OnProductsFetched += HandleProductsFetched;
            _controller.OnProductsFetchFailed += HandleProductsFetchFailed;
            _controller.OnPurchasesFetched += HandlePurchasesFetched;
            _controller.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;
            _controller.OnPurchasePending += HandlePurchasePending;
            _controller.OnPurchaseFailed += HandlePurchaseFailed;
            _controller.OnPurchaseDeferred += HandlePurchaseDeferred;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            _controller.OnStoreConnected -= HandleStoreConnected;
            _controller.OnStoreDisconnected -= HandleStoreDisconnected;
            _controller.OnProductsFetched -= HandleProductsFetched;
            _controller.OnProductsFetchFailed -= HandleProductsFetchFailed;
            _controller.OnPurchasesFetched -= HandlePurchasesFetched;
            _controller.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;
            _controller.OnPurchasePending -= HandlePurchasePending;
            _controller.OnPurchaseFailed -= HandlePurchaseFailed;
            _controller.OnPurchaseDeferred -= HandlePurchaseDeferred;

            _subscribed = false;
        }

        private void HandleStoreConnected()
        {
            if (_verboseLogging) Debug.Log("[IAPService] 스토어에 연결됐다.");

            _connecting?.TrySetResult(true);
        }

        private void HandleStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            Debug.LogWarning($"[IAPService] 스토어 연결이 끊겼다: {failure.message}");

            // 연결 대기 중이었다면 실패로 끝낸다. 이미 연결된 뒤의 끊김은 SDK가 재연결을 맡는다.
            _connecting?.TrySetResult(false);
        }

        private void HandleProductsFetched(List<UnityProduct> products)
        {
            _products.Clear();
            _productsByStoreId.Clear();

            foreach (var product in products)
            {
                var storeId = product.definition?.id;
                if (string.IsNullOrEmpty(storeId)) continue;

                _productsByStoreId[storeId] = product;
                _products.Add(ToIapProduct(storeId, product));
            }

            if (_verboseLogging) Debug.Log($"[IAPService] 상품 {_products.Count}개를 조회했다.");

            _fetchingProducts?.TrySetResult(true);
        }

        private void HandleProductsFetchFailed(ProductFetchFailed failure)
        {
            Debug.LogError($"[IAPService] 상품 조회에 실패했다: {failure.FailureReason}");

            _fetchingProducts?.TrySetResult(false);
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            // 앱이 죽어 확정하지 못했던 구매들. 지급되지 않았을 수 있으므로 전부 다시 흘려보낸다.
            foreach (var order in orders.PendingOrders) EmitPending(order, isRestored: true);

            _fetchingPurchases?.TrySetResult(true);
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[IAPService] 이전 구매 조회에 실패했다: {failure.message}");

            _fetchingPurchases?.TrySetResult(false);
        }

        private void HandlePurchasePending(PendingOrder order) => EmitPending(order, _restoreInProgress);

        private void HandlePurchaseFailed(FailedOrder order)
        {
            var storeId = StoreIdOf(order);
            if (string.IsNullOrEmpty(storeId)) return;

            var cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;

            PurchaseFailed?.Invoke(new IapPurchaseFailure(storeId, cancelled,
                new IapError((int)order.FailureReason, order.Details ?? order.FailureReason.ToString())));
        }

        private void HandlePurchaseDeferred(DeferredOrder order)
        {
            var storeId = StoreIdOf(order);
            if (string.IsNullOrEmpty(storeId)) return;

            PurchaseDeferred?.Invoke(storeId);
        }

        private void EmitPending(PendingOrder order, bool isRestored)
        {
            var storeId = StoreIdOf(order);
            if (string.IsNullOrEmpty(storeId)) return;

            var transactionId = order.Info?.TransactionID;

            if (string.IsNullOrEmpty(transactionId))
            {
                Debug.LogWarning($"[IAPService] 트랜잭션 ID가 없는 구매가 도착했다: {storeId}. 확정할 수 없어 건너뛴다.");
                return;
            }

            _unconfirmed[transactionId] = order;

            PurchasePending?.Invoke(new IapPendingPurchase(storeId, transactionId, order.Info?.Receipt, isRestored));
        }

        private static string StoreIdOf(Order order)
        {
            var items = order?.CartOrdered?.Items();
            if (items == null || items.Count == 0) return null;

            return items[0]?.Product?.definition?.id;
        }

        private IapProduct ToIapProduct(string storeId, UnityProduct product)
        {
            var id = storeId;
            var type = IapProductType.Consumable;

            // 공용 ID와 타입은 우리 카탈로그가 진실이다 — 스토어는 우리가 붙인 이름을 모른다.
            if (_catalog != null)
            {
                foreach (var definition in _catalog)
                {
                    if (definition.StoreId != storeId) continue;

                    id = definition.Id;
                    type = definition.Type;
                    break;
                }
            }

            var metadata = product.metadata;

            return new IapProduct(id, storeId, type,
                                  metadata?.localizedTitle,
                                  metadata?.localizedDescription,
                                  metadata?.localizedPriceString,
                                  metadata == null ? 0.0 : (double)metadata.localizedPrice,
                                  metadata?.isoCurrencyCode,
                                  product.availableToPurchase);
        }

        private static UnityProductType ToUnityType(IapProductType type) =>
            type == IapProductType.NonConsumable ? UnityProductType.NonConsumable : UnityProductType.Consumable;

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
