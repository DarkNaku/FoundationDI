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

        public async Awaitable<bool> InitializeAsync()
        {
            // 미확정 주문을 SDK가 임의로 재처리하지 않게 한다. 확정 시점은 이 서비스 소관이고,
            // 재전달된 주문에는 "복원됨" 표시를 붙여야 하는데 자동 경로로는 그 구분이 사라진다.
            _purchaseService.ProcessPendingOrdersOnPurchasesFetched(false);

            // Unity IAP는 Connect 전에 구독을 끝낼 것을 요구하고, 미확정 구매가 연결 직후에
            // 재전달되기도 한다.
            Subscribe();

            if (!await ConnectAsync() || !await FetchProductsAsync())
            {
                Debug.LogError("[IAPService] Unity IAP 초기화에 실패했다.");
                return false;
            }

            // 실패해도 치명적이지 않다 — 미확정 구매를 이번 실행에 못 찾을 뿐이고 다음 실행에 다시 시도한다.
            await FetchPurchasesAsync();

            IsInitialized = true;
            return true;
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

        public bool IsOwned(string productId) => false;

        public Awaitable<IAPPurchaseResult> PurchaseAsync(string productId) =>
            Completed(IAPPurchaseResult.NotReady());

        public Awaitable<IAPRestoreResult> RestoreAsync() =>
            Completed(IAPRestoreResult.Fail(default));

        public void Dispose()
        {
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

        private void HandlePurchasesFetched(Orders orders) => _fetchingPurchases?.TrySetResult(true);

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[IAPService] 이전 구매 조회에 실패했다: {failure?.message}");

            _fetchingPurchases?.TrySetResult(false);
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
