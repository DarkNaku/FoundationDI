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

        public IReadOnlyList<IAPProduct> Products => Array.Empty<IAPProduct>();

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

            if (!await ConnectAsync()) return false;
            if (!await FetchProductsAsync()) return false;

            // 실패해도 치명적이지 않다 — 미확정 구매를 이번 실행에 못 찾을 뿐이고 다음 실행에 다시 시도한다.
            await FetchPurchasesAsync();

            IsInitialized = true;
            return true;
        }

        public bool TryGetProduct(string productId, out IAPProduct product)
        {
            product = default;
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
            _productService.OnProductsFetched += HandleProductsFetched;
            _purchaseService.OnPurchasesFetched += HandlePurchasesFetched;

            _subscribed = true;
        }

        private void HandleStoreConnected() => _connecting?.TrySetResult(true);

        private void HandleProductsFetched(List<Product> products) => _fetchingProducts?.TrySetResult(true);

        private void HandlePurchasesFetched(Orders orders) => _fetchingPurchases?.TrySetResult(true);

        private static Awaitable<T> Completed<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
