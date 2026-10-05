using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace DarkNaku.FoundationDI
{
    // Unity IAP를 직접 호출하는 IIAPService 구현.
    // Unity IAP만으로는 매번 직접 맞춰야 하는 규율(검증 → 지급 → 확정, 소유 동기화, 복원 완료 시점,
    // 이중 처리 방지)을 여기 한 곳에 둔다.
    public sealed class UnityIAPService : IIAPService
    {
        // 지급 실패는 스토어 실패와 구분되는 원인이지만 결과 enum을 늘리는 대신 에러 코드로 남긴다 —
        // 호출부가 분기할 이유가 없다(둘 다 "이번엔 못 줬다"이고 재시도는 스토어가 알아서 한다).
        internal const int FulfillmentFailedCode = -1001;

        private readonly IStoreService _store;
        private readonly IProductService _productService;
        private readonly IPurchaseService _purchaseService;
        private readonly List<ProductDefinition> _catalog;
        private readonly IIAPFulfillment _fulfillment;
        private readonly IReceiptValidator _validator;
        private readonly IEntitlementStorage _entitlements;
        private readonly bool _verboseLogging;

        // 조회된 상품. 게임 코드에 내보낼 값과 구매에 쓸 Unity 객체를 공용 ID로 함께 들고 있는다.
        private readonly List<IAPProduct> _products = new();
        private readonly Dictionary<string, Product> _unityProducts = new();

        // 진행 중인 초기화에 편승한 호출자들. Awaitable은 단일 사용이라 하나를 여럿이 await 할 수
        // 없으므로, 호출자마다 별도의 완료 소스를 만들어 두었다가 한꺼번에 같은 결과로 완료시킨다.
        private readonly List<AwaitableCompletionSource<bool>> _initWaiters = new();
        private bool _initializing;

        // 대기 중인 PurchaseAsync. 스토어 UI가 모달이라 동시에 두 개가 뜨는 일은 없고,
        // 주문 이벤트에는 "어느 호출의 결과인가"가 실려 오지 않으므로 하나만 허용한다.
        private AwaitableCompletionSource<IAPPurchaseResult> _pendingSource;
        private string _pendingProductId;

        private AwaitableCompletionSource<bool> _connecting;
        private AwaitableCompletionSource<bool> _fetchingProducts;
        private AwaitableCompletionSource<bool> _fetchingPurchases;

        private bool _subscribed;

        internal UnityIAPService(IStoreService store, IProductService products, IPurchaseService purchases,
                                 IReadOnlyList<ProductDefinition> catalog,
                                 IIAPFulfillment fulfillment, IReceiptValidator validator,
                                 IEntitlementStorage entitlements, bool verboseLogging)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _productService = products ?? throw new ArgumentNullException(nameof(products));
            _purchaseService = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _catalog = catalog != null ? new List<ProductDefinition>(catalog) : new List<ProductDefinition>();
            _fulfillment = fulfillment ?? new AutoConfirmFulfillment();
            _validator = validator ?? new CrossPlatformReceiptValidator();
            _entitlements = entitlements ?? new PlayerPrefsEntitlementStorage();
            _verboseLogging = verboseLogging;
        }

        public bool IsInitialized { get; private set; }

        public IReadOnlyList<IAPProduct> Products =>
            IsInitialized ? _products : Array.Empty<IAPProduct>();

        public event Action<IAPPurchase> Purchased;
        public event Action<string> OwnedChanged;

        public Awaitable<bool> InitializeAsync()
        {
            if (IsInitialized) return Completed(true);

            if (_initializing)
            {
                var waiter = new AwaitableCompletionSource<bool>();
                _initWaiters.Add(waiter);
                return waiter.Awaitable;
            }

            _initializing = true;
            return RunInitializeAsync();
        }

        private async Awaitable<bool> RunInitializeAsync()
        {
            // 미확정 주문을 SDK가 임의로 재처리하지 않게 한다. 확정 시점은 이 서비스 소관이고,
            // 재전달된 주문에는 "복원됨" 표시를 붙여야 하는데 자동 경로로는 그 구분이 사라진다.
            _purchaseService.ProcessPendingOrdersOnPurchasesFetched(false);

            // Unity IAP는 Connect 전에 구독을 끝낼 것을 요구하고, 미확정 구매가 연결 직후에
            // 재전달되기도 한다.
            Subscribe();

            var success = await ConnectAsync() && await FetchProductsAsync();

            if (success)
            {
                // 실패해도 치명적이지 않다 — 미확정 구매를 이번 실행에 못 찾을 뿐이고 다음 실행에 다시 시도한다.
                await FetchPurchasesAsync();
            }
            else
            {
                Debug.LogError("[IAPService] Unity IAP 초기화에 실패했다.");
            }

            IsInitialized = success;
            _initializing = false;

            CompleteInitWaiters(success);
            return success;
        }

        public bool TryGetProduct(string productId, out IAPProduct product)
        {
            product = default;

            if (!IsInitialized || string.IsNullOrEmpty(productId)) return false;

            foreach (var candidate in _products)
            {
                if (candidate.Id != productId) continue;

                product = candidate;
                return true;
            }

            return false;
        }

        public bool IsOwned(string productId) =>
            !string.IsNullOrEmpty(productId) && _entitlements.IsOwned(productId);

        public async Awaitable<IAPPurchaseResult> PurchaseAsync(string productId)
        {
            if (!IsInitialized) return IAPPurchaseResult.NotReady();

            if (string.IsNullOrEmpty(productId) || !_unityProducts.TryGetValue(productId, out var product))
            {
                Debug.LogWarning($"[IAPService] 카탈로그에 없는 상품을 구매하려 했다: {productId}");
                return IAPPurchaseResult.NotReady();
            }

            // 비소모성은 스토어가 "이미 소유"로 거절하기 전에 먼저 답한다.
            // 스토어 시트를 띄웠다가 에러로 닫히는 것보다 낫다.
            if (product.definition.type == ProductType.NonConsumable && IsOwned(productId))
            {
                return IAPPurchaseResult.AlreadyOwned(OwnedPurchaseOf(product));
            }

            if (_pendingSource != null) return IAPPurchaseResult.NotReady();

            _pendingSource = new AwaitableCompletionSource<IAPPurchaseResult>();
            _pendingProductId = productId;

            // 조회한 Product로 산다 — catalogListingId 규칙에 의존하지 않는다.
            _purchaseService.PurchaseProduct(product);

            return await _pendingSource.Awaitable;
        }

        public Awaitable<IAPRestoreResult> RestoreAsync() =>
            Completed(IAPRestoreResult.Fail(default));

        public void Dispose()
        {
        }

        private void CompleteInitWaiters(bool success)
        {
            if (_initWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<bool>>(_initWaiters);
            _initWaiters.Clear();

            foreach (var waiter in waiters) waiter.SetResult(success);
        }

        private Awaitable<bool> ConnectAsync()
        {
            _connecting = new AwaitableCompletionSource<bool>();

            // Connect()가 돌려주는 Task를 직접 await 하지 않는다. 성공/실패가 모두
            // OnStoreConnected/OnStoreDisconnected로 오기 때문에 이벤트 하나로 받는 편이
            // 스레드 문맥에 대한 가정이 없다.
            _ = _store.Connect();

            return _connecting.Awaitable;
        }

        private Awaitable<bool> FetchProductsAsync()
        {
            _fetchingProducts = new AwaitableCompletionSource<bool>();
            _productService.FetchProducts(new List<ProductDefinition>(_catalog));
            return _fetchingProducts.Awaitable;
        }

        private Awaitable<bool> FetchPurchasesAsync()
        {
            _fetchingPurchases = new AwaitableCompletionSource<bool>();
            _purchaseService.FetchPurchases();
            return _fetchingPurchases.Awaitable;
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            _store.OnStoreConnected += HandleStoreConnected;
            _store.OnStoreDisconnected += HandleStoreDisconnected;
            _productService.OnProductsFetched += HandleProductsFetched;
            _productService.OnProductsFetchFailed += HandleProductsFetchFailed;
            _purchaseService.OnPurchasesFetched += HandlePurchasesFetched;
            _purchaseService.OnPurchasePending += HandlePurchasePending;
            _purchaseService.OnPurchaseFailed += HandlePurchaseFailed;
            _purchaseService.OnPurchaseDeferred += HandlePurchaseDeferred;
            _purchaseService.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;

            _subscribed = true;
        }

        private void HandleStoreConnected() => _connecting?.TrySetResult(true);

        private void HandleStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            Debug.LogWarning($"[IAPService] 스토어 연결이 끊겼다: {failure?.message}");

            // 연결 대기 중이었다면 실패로 끝낸다. 이미 연결된 뒤의 끊김은 SDK가 재연결을 맡는다.
            _connecting?.TrySetResult(false);
        }

        private void HandleProductsFetched(List<Product> products)
        {
            _products.Clear();
            _unityProducts.Clear();

            foreach (var product in products)
            {
                var id = product?.definition?.id;
                if (string.IsNullOrEmpty(id)) continue;

                _unityProducts[id] = product;
                _products.Add(ToIAPProduct(product));
            }

            if (_verboseLogging) Debug.Log($"[IAPService] 상품 {_products.Count}개를 조회했다.");

            _fetchingProducts?.TrySetResult(true);
        }

        private void HandleProductsFetchFailed(ProductFetchFailed failure)
        {
            Debug.LogError($"[IAPService] 상품 조회에 실패했다: {failure?.FailureReason}");

            _fetchingProducts?.TrySetResult(false);
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            // 앱이 죽어 확정하지 못했던 구매들. 지급되지 않았을 수 있으므로 다시 흘려보낸다.
            foreach (var order in orders.PendingOrders) ProcessPending(order, isRestored: true);

            var owned = new HashSet<string>();

            foreach (var order in orders.ConfirmedOrders)
            {
                var productId = ProductOf(order)?.definition?.id;
                if (!string.IsNullOrEmpty(productId)) owned.Add(productId);
            }

            SyncOwnership(owned);

            _fetchingPurchases?.TrySetResult(true);
        }

        // 스토어의 확정 목록이 진실이다. 목록에 있으면 소유(재설치·기기 변경), 없으면 회수(환불)한다.
        // 이미 확정·지급된 구매이므로 지급 파이프라인을 다시 타지 않는다.
        private void SyncOwnership(HashSet<string> owned)
        {
            foreach (var definition in _catalog)
            {
                if (definition.type != ProductType.NonConsumable) continue;

                var isOwned = owned.Contains(definition.id);
                if (_entitlements.IsOwned(definition.id) == isOwned) continue;

                _entitlements.SetOwned(definition.id, isOwned);
                OwnedChanged?.Invoke(definition.id);
            }
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[IAPService] 이전 구매 조회에 실패했다: {failure?.message}");

            _fetchingPurchases?.TrySetResult(false);
        }

        private void HandlePurchasePending(PendingOrder order) => ProcessPending(order, isRestored: false);

        // iOS Ask-to-Buy 등. 승인되면 나중에 OnPurchasePending으로 들어와 같은 파이프라인을 탄다.
        private void HandlePurchaseDeferred(DeferredOrder order)
        {
            var productId = ProductOf(order)?.definition?.id;
            if (string.IsNullOrEmpty(productId)) return;

            CompletePendingFor(productId, IAPPurchaseResult.Deferred());
        }

        private void HandlePurchaseFailed(FailedOrder order)
        {
            var productId = ProductOf(order)?.definition?.id;
            if (string.IsNullOrEmpty(productId)) return;

            var cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;

            // 취소는 에러가 아니다. 로그로 남기면 정상 동작이 매번 소음이 된다.
            if (cancelled)
            {
                CompletePendingFor(productId, IAPPurchaseResult.UserCancelled());
                return;
            }

            var error = new IAPError((int)order.FailureReason, order.Details ?? order.FailureReason.ToString());
            Debug.LogWarning($"[IAPService] 구매에 실패했다: {productId} {error}");

            CompletePendingFor(productId, IAPPurchaseResult.Failed(error));
        }

        // 주문 이벤트 핸들러라 반환값을 기다릴 주체가 없다 — 예외는 전부 안에서 잡는다.
        private async void ProcessPending(PendingOrder order, bool isRestored)
        {
            var product = ProductOf(order);
            var productId = product?.definition?.id;
            var transactionId = order.Info?.TransactionID;

            if (!IsInCatalog(productId))
            {
                // 확정해 버리면 이 구매는 영영 지급할 수 없다. 다음 버전의 카탈로그가
                // 이 상품을 알게 될 수도 있으므로 미확정으로 남긴다.
                Debug.LogWarning($"[IAPService] 카탈로그에 없는 상품의 구매가 도착했다: {productId}. " +
                                 "확정하지 않고 남겨둔다.");
                return;
            }

            if (string.IsNullOrEmpty(transactionId))
            {
                Debug.LogWarning($"[IAPService] 트랜잭션 ID가 없는 구매가 도착했다: {productId}. 확정할 수 없어 건너뛴다.");
                return;
            }

            var purchase = BuildPurchase(product, order, isRestored);

            if (!_validator.Validate(purchase, out var validationError))
            {
                Debug.LogError($"[IAPService] 영수증 검증에 실패했다: {purchase.ProductId} {validationError}");
                CompletePendingFor(productId, IAPPurchaseResult.InvalidReceipt(validationError));
                return;
            }

            bool fulfilled;

            try
            {
                fulfilled = await _fulfillment.FulfillAsync(purchase);
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAPService] 지급 핸들러가 예외를 던졌다. 확정하지 않는다: {e}");
                fulfilled = false;
            }

            if (!fulfilled)
            {
                // 확정하지 않는다 — 스토어가 다음 실행에 같은 구매를 다시 내려준다.
                CompletePendingFor(productId, IAPPurchaseResult.Failed(
                    new IAPError(FulfillmentFailedCode, "지급에 실패해 확정하지 않았다")));
                return;
            }

            _purchaseService.ConfirmPurchase(order);

            if (purchase.Type == IAPProductType.NonConsumable && !_entitlements.IsOwned(productId))
            {
                _entitlements.SetOwned(productId, true);
                OwnedChanged?.Invoke(productId);
            }

            Purchased?.Invoke(purchase);

            CompletePendingFor(productId, IAPPurchaseResult.Purchased(purchase));
        }

        private IAPPurchase BuildPurchase(Product product, Order order, bool isRestored)
        {
            var definition = product.definition;
            var metadata = product.metadata;

            return new IAPPurchase(definition.id, ToProductType(definition.type),
                                   order.Info?.TransactionID, order.Info?.Receipt,
                                   metadata == null ? 0.0 : (double)metadata.localizedPrice,
                                   metadata?.isoCurrencyCode ?? string.Empty, isRestored);
        }

        // 이미 소유한 상품을 다시 사려 할 때 돌려줄 페이로드. 거래 정보는 없다 —
        // 이번 세션에 일어난 거래가 아니기 때문이다.
        private static IAPPurchase OwnedPurchaseOf(Product product)
        {
            var metadata = product.metadata;

            return new IAPPurchase(product.definition.id, ToProductType(product.definition.type), null, null,
                                   metadata == null ? 0.0 : (double)metadata.localizedPrice,
                                   metadata?.isoCurrencyCode ?? string.Empty, true);
        }

        // 대기 중인 구매의 상품과 일치할 때만 완료시킨다. 재전달·복원처럼 아무도 기다리지
        // 않는 구매는 조용히 지나간다.
        private void CompletePendingFor(string productId, IAPPurchaseResult result)
        {
            if (_pendingSource == null || _pendingProductId != productId) return;

            var source = _pendingSource;
            _pendingSource = null;
            _pendingProductId = null;
            source.SetResult(result);
        }

        private bool IsInCatalog(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return false;

            foreach (var definition in _catalog)
            {
                if (definition.id == productId) return true;
            }

            return false;
        }

        private static Product ProductOf(Order order)
        {
            var items = order?.CartOrdered?.Items();
            if (items == null || items.Count == 0) return null;

            return items[0]?.Product;
        }

        private static IAPProduct ToIAPProduct(Product product)
        {
            var definition = product.definition;
            var metadata = product.metadata;

            return new IAPProduct(definition.id, definition.storeSpecificId, ToProductType(definition.type),
                                  metadata?.localizedTitle,
                                  metadata?.localizedDescription,
                                  metadata?.localizedPriceString,
                                  metadata == null ? 0.0 : (double)metadata.localizedPrice,
                                  metadata?.isoCurrencyCode,
                                  product.availableToPurchase);
        }

        private static IAPProductType ToProductType(ProductType type) =>
            type == ProductType.NonConsumable ? IAPProductType.NonConsumable : IAPProductType.Consumable;

        private static Awaitable<T> Completed<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
