using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace DarkNaku.FoundationDI
{
    // Unity IAP를 직접 호출하는 IIAPService 구현.
    // Unity IAP만으로는 매번 직접 맞춰야 하는 규율(검증 → 지급 → 확정, 소유 동기화, 복원 완료 시점,
    // 이중 처리 방지)을 여기 한 곳에 둔다.
    public sealed class IAPService : IIAPService
    {
        private readonly IStoreService _store;
        private readonly IProductService _productService;
        private readonly IPurchaseService _purchaseService;
        private readonly List<ProductDefinition> _catalog;
        private readonly IIAPFulfillment _fulfillment;
        private readonly IReceiptValidator _validator;
        private readonly IEntitlementStorage _entitlements;
        private readonly IAPServiceOptions _options;
        private readonly Func<IMainThreadDispatcher> _dispatcherFactory;
        private IMainThreadDispatcher _dispatcher;
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

        // 지급이 진행 중인 주문(트랜잭션 ID). 그 사이 조회가 같은 주문을 미확정으로 또 실어 와도
        // 두 번 지급하지 않는 기준이다. ProcessPendingOrdersOnPurchasesFetched(false)로 SDK의
        // 같은 장치(세션 처리 목록)를 꺼 뒀기 때문에 직접 한다.
        private readonly HashSet<string> _inFlight = new();

        // 이번 세션에 확정한 트랜잭션. 확정은 스토어에 비동기로 반영되므로 직후의 조회가 같은 주문을
        // 아직 미확정으로 돌려줄 수 있다 — 다시 지급하면 이중 지급이고, 소유 목록에서 빠지면 회수된다.
        private readonly HashSet<string> _confirmedInSession = new();

        // 지급 처리기(IIAPFulfillmentGate)가 준비되기 전에 온 주문. 트랜잭션 ID마다 하나, 온 순서대로.
        // 확정하지 않았으므로 앱이 죽어도 스토어가 다음 실행에 다시 준다 — 이 목록은 이번 실행 안의 지름길이다.
        private readonly List<HeldOrder> _held = new();

        // ProcessUnconfirmedAsync가 기다리는 조회. 여러 번 불려도 조회는 하나만 진행한다.
        private readonly List<AwaitableCompletionSource> _refetchWaiters = new();
        private bool _refetching;

        // 복원. RestoreTransactions의 콜백과 뒤따르는 조회 결과가 둘 다 와야 끝난다 — 순서는 스토어마다
        // 다를 수 있어서, 먼저 온 쪽을 기억해 두었다가 나중에 온 쪽에서 끝낸다.
        private AwaitableCompletionSource<bool> _restoring;
        private bool _restoreCallbackSucceeded;
        private bool? _restoreFetchResult;

        // RestoreAsync가 진행되는 동안 복원된 구매의 수 — 스토어가 확정 목록으로 돌려준 비소모성과
        // 파이프라인을 통과한 미확정 구매를 합친다.
        private int _restoredCount;

        private AwaitableCompletionSource<bool> _connecting;
        private AwaitableCompletionSource<bool> _fetchingProducts;
        private AwaitableCompletionSource<bool> _fetchingPurchases;

        private bool _subscribed;
        private bool _disposed;

        // 기본 경로. StoreController 하나가 세 인터페이스를 모두 구현하고, 상품은 Unity IAP Catalog
        // (Assets/Resources/IAPProductCatalog.json)에서 읽는다. 생성만으로는 스토어에 연결하지 않는다.
        public IAPService(IIAPFulfillment fulfillment = null,
                               IReceiptValidator validator = null,
                               IEntitlementStorage entitlements = null,
                               bool verboseLogging = false)
            : this(new IAPServiceDependencies
            {
                Fulfillment = fulfillment,
                Validator = validator,
                Entitlements = entitlements,
                Options = new IAPServiceOptions { VerboseLogging = verboseLogging },
            })
        {
        }

        internal IAPService(IAPServiceDependencies dependencies)
            : this(UnityIAPServices.StoreController(), LoadCatalog(), dependencies)
        {
        }

        private IAPService(StoreController controller, IReadOnlyList<ProductDefinition> catalog,
                                IAPServiceDependencies dependencies)
            : this(controller, controller, controller, catalog, dependencies)
        {
        }

        internal IAPService(IStoreService store, IProductService products, IPurchaseService purchases,
                                 IReadOnlyList<ProductDefinition> catalog,
                                 IIAPFulfillment fulfillment, IReceiptValidator validator,
                                 IEntitlementStorage entitlements, bool verboseLogging)
            : this(store, products, purchases, catalog, new IAPServiceDependencies
            {
                Fulfillment = fulfillment,
                Validator = validator,
                Entitlements = entitlements,
                Options = new IAPServiceOptions { VerboseLogging = verboseLogging },
            })
        {
        }

        internal IAPService(IStoreService store, IProductService products, IPurchaseService purchases,
                                 IReadOnlyList<ProductDefinition> catalog, IAPServiceDependencies dependencies)
        {
            dependencies ??= new IAPServiceDependencies();

            _store = store ?? throw new ArgumentNullException(nameof(store));
            _productService = products ?? throw new ArgumentNullException(nameof(products));
            _purchaseService = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _catalog = catalog != null ? new List<ProductDefinition>(catalog) : new List<ProductDefinition>();
            _fulfillment = dependencies.Fulfillment ?? new AutoConfirmFulfillment();
            _validator = dependencies.Validator ?? new CrossPlatformReceiptValidator();
            _entitlements = dependencies.Entitlements ?? new PlayerPrefsEntitlementStorage();
            _options = dependencies.Options ?? new IAPServiceOptions();
            _dispatcherFactory = dependencies.Dispatcher;
            _verboseLogging = _options.VerboseLogging;
        }

        public bool IsInitialized { get; private set; }

        public IReadOnlyList<IAPProduct> Products =>
            IsInitialized ? _products : Array.Empty<IAPProduct>();

        public event Action<IAPPurchase> Purchased;
        public event Action<string> OwnedChanged;

        public Awaitable<bool> InitializeAsync()
        {
            if (_disposed) return Completed(false);
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
            if (_disposed || !IsInitialized) return IAPPurchaseResult.NotReady();

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

            // 지역 변수로 잡아 둔다. 스토어가 PurchaseProduct 안에서 곧바로 주문을 내면(에디터의 FakeStore)
            // 완료 처리가 필드를 먼저 비운다.
            var source = new AwaitableCompletionSource<IAPPurchaseResult>();
            _pendingSource = source;
            _pendingProductId = productId;

            // 조회한 Product로 산다 — catalogListingId 규칙에 의존하지 않는다.
            _purchaseService.PurchaseProduct(product);

            var seconds = _options.PurchaseTimeoutSeconds;

            using var timeout = StartTimeout(seconds, () =>
            {
                if (_pendingSource != source) return;

                Debug.LogWarning($"[IAPService] 구매 결과가 {seconds}초 안에 오지 않았다: {productId}. " +
                                 "결제가 끝났다면 늦게라도 지급된다.");
                CompletePending(IAPPurchaseResult.Failed(new IAPError(IAPErrorCodes.Timeout, "구매 결과를 기다리다 시간이 초과됐다")));
            });

            return await source.Awaitable;
        }

        // RestoreTransactions의 콜백은 복원 결과가 아니다. SDK는 성공하면 FetchPurchases()를 다시
        // 부르고 곧바로 콜백을 부르므로(PurchaseService.RestoreTransactions), 복원된 구매는 콜백 뒤의
        // OnPurchasesFetched로 온다.
        public async Awaitable<IAPRestoreResult> RestoreAsync()
        {
            if (_disposed || !IsInitialized)
            {
                return IAPRestoreResult.Fail(new IAPError(IAPErrorCodes.NotInitialized, "초기화되지 않았다"));
            }

            if (_restoring != null)
            {
                Debug.LogWarning("[IAPService] 복원이 이미 진행 중이다.");
                return IAPRestoreResult.Fail(new IAPError(IAPErrorCodes.RestoreInProgress, "복원이 이미 진행 중이다"));
            }

            _restoredCount = 0;
            _restoreCallbackSucceeded = false;
            _restoreFetchResult = null;

            var source = new AwaitableCompletionSource<bool>();
            _restoring = source;

            _purchaseService.RestoreTransactions((success, error) =>
            {
                if (_restoring != source) return;

                if (!success)
                {
                    Debug.LogWarning($"[IAPService] 복원에 실패했다: {error}");
                    FinishRestore(false);
                    return;
                }

                _restoreCallbackSucceeded = true;

                if (_restoreFetchResult.HasValue) FinishRestore(_restoreFetchResult.Value);
            });

            if (!await source.Awaitable)
            {
                return IAPRestoreResult.Fail(new IAPError(IAPErrorCodes.RestoreFailed, "복원에 실패했다"));
            }

            return IAPRestoreResult.Ok(_restoredCount);
        }

        public Awaitable ProcessUnconfirmedAsync()
        {
            if (_disposed || !IsInitialized) return CompletedAwaitable();

            ReleaseHeld();

            var waiter = new AwaitableCompletionSource();
            _refetchWaiters.Add(waiter);

            // 복원이 조회 결과를 기다리는 중이면 거기에 편승한다 — 미확정 구매는 그 조회에도 실려 온다.
            var restoreFetching = _restoring != null && !_restoreFetchResult.HasValue;

            if (!_refetching && !restoreFetching)
            {
                _refetching = true;
                _purchaseService.FetchPurchases();
            }

            return waiter.Awaitable;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Unsubscribe();

            // 대기 중인 구매·복원이 영원히 매달리지 않게 끊어준다.
            CompletePending(IAPPurchaseResult.Failed(new IAPError(IAPErrorCodes.Disposed, "서비스가 해제됐다")));
            FinishRestore(false);
            CompleteInitWaiters(false);

            _held.Clear();
            _refetching = false;
            CompleteRefetchWaiters();
        }

        private async Awaitable<bool> RunInitializeAsync()
        {
            // 미확정 주문을 SDK가 임의로 재처리하지 않게 한다. 확정 시점은 이 서비스 소관이고,
            // 재전달된 주문에는 "복원됨" 표시를 붙여야 하는데 자동 경로로는 그 구분이 사라진다.
            _purchaseService.ProcessPendingOrdersOnPurchasesFetched(false);

            WarnIfConsumablesAutoConfirmed();

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

        private static List<ProductDefinition> LoadCatalog()
        {
            var catalog = ProductCatalog.LoadDefaultCatalog();

            UnityIAPCatalog.WarnIfCodelessAutoInitialization(catalog);

            if (catalog.IsEmpty())
            {
                Debug.LogWarning("[IAPService] IAP Catalog가 비어 있다. Services > In-App Purchasing > IAP Catalog에서 상품을 추가할 것.");
            }

            return UnityIAPCatalog.ToDefinitions(catalog, CurrentStoreName());
        }

        // 에디터에서는 빌드 타깃을 따라간다 — 에디터 실행 중에도 현재 타깃의 스토어 ID가 나온다.
        private static string CurrentStoreName()
        {
#if UNITY_ANDROID
            return GooglePlay.Name;
#elif UNITY_IOS
            return AppleAppStore.Name;
#else
            return null;
#endif
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
            var source = new AwaitableCompletionSource<bool>();
            _connecting = source;

            // Connect()가 돌려주는 Task를 직접 await 하지 않는다. 성공/실패가 모두
            // OnStoreConnected/OnStoreDisconnected로 오기 때문에 이벤트 하나로 받는 편이
            // 스레드 문맥에 대한 가정이 없다.
            _ = _store.Connect();

            return WithTimeout(source, _options.ConnectTimeoutSeconds, "스토어 연결");
        }

        private Awaitable<bool> FetchProductsAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            _fetchingProducts = source;
            _products.Clear();
            _unityProducts.Clear();

            _productService.FetchProducts(new List<ProductDefinition>(_catalog));

            return WithTimeout(source, _options.FetchTimeoutSeconds, "상품 조회");
        }

        private Awaitable<bool> FetchPurchasesAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            _fetchingPurchases = source;
            _purchaseService.FetchPurchases();

            return WithTimeout(source, _options.FetchTimeoutSeconds, "구매 조회");
        }

        // 늦게 온 이벤트는 그대로 처리된다 — 완료 소스만 먼저 끝낸다. 연결은 다음 초기화가 새 소스로 다시 시도한다.
        private async Awaitable<bool> WithTimeout(AwaitableCompletionSource<bool> source, float seconds, string what)
        {
            // 완료되면 곧바로 이 시간 제한이 해제되므로 콜백이 불렸다면 아직 끝나지 않은 것이다. 로그를 먼저 남긴다 —
            // 완료가 동기로 이어져 초기화 실패 로그가 먼저 찍히면 원인이 뒤에 보인다.
            using var timeout = StartTimeout(seconds, () =>
            {
                Debug.LogWarning($"[IAPService] {what}이 {seconds}초 안에 끝나지 않았다.");
                source.TrySetResult(false);
            });

            return await source.Awaitable;
        }

        // 디스패처가 없으면(직접 생성) 시간 제한 없이 기다린다.
        private IDisposable StartTimeout(float seconds, Action onTimeout)
        {
            if (seconds <= 0f || _disposed) return null;

            _dispatcher ??= _dispatcherFactory?.Invoke();

            return _dispatcher?.Delay(seconds, onTimeout);
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
            _purchaseService.OnPurchaseConfirmed += HandlePurchaseConfirmed;
            _purchaseService.OnPurchaseFailed += HandlePurchaseFailed;
            _purchaseService.OnPurchaseDeferred += HandlePurchaseDeferred;
            _purchaseService.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            _store.OnStoreConnected -= HandleStoreConnected;
            _store.OnStoreDisconnected -= HandleStoreDisconnected;
            _productService.OnProductsFetched -= HandleProductsFetched;
            _productService.OnProductsFetchFailed -= HandleProductsFetchFailed;
            _purchaseService.OnPurchasesFetched -= HandlePurchasesFetched;
            _purchaseService.OnPurchasePending -= HandlePurchasePending;
            _purchaseService.OnPurchaseConfirmed -= HandlePurchaseConfirmed;
            _purchaseService.OnPurchaseFailed -= HandlePurchaseFailed;
            _purchaseService.OnPurchaseDeferred -= HandlePurchaseDeferred;
            _purchaseService.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;

            _subscribed = false;
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

        // Unity IAP 5.4는 일부만 받으면 받은 것으로 OnProductsFetched를 먼저, 빠진 것으로 이 이벤트를 뒤에 쏜다.
        // 스토어 콘솔에 없는(심사 전·삭제된) 상품 하나 때문에 상점 전체를 닫지 않는다 — 받은 것이 있으면 진행한다.
        private void HandleProductsFetchFailed(ProductFetchFailed failure)
        {
            var failed = failure?.FailedFetchProducts;
            var failedCount = failed?.Count ?? 0;
            var isPartial = _products.Count > 0 || (failedCount > 0 && failedCount < _catalog.Count);

            if (!isPartial)
            {
                Debug.LogError($"[IAPService] 상품 조회에 실패했다: {failure?.FailureReason}");
                _fetchingProducts?.TrySetResult(false);
                return;
            }

            var ids = new List<string>();
            if (failed != null)
            {
                foreach (var definition in failed) ids.Add(definition?.id);
            }

            Debug.LogWarning($"[IAPService] 일부 상품을 조회하지 못해 빼고 진행한다(스토어 콘솔에 없거나 비활성): " +
                             string.Join(", ", ids));

            // 순서가 뒤집혀 받은 상품이 아직 오지 않았으면 그걸 기다린다(오지 않으면 시간 제한이 끝낸다).
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            var owned = new HashSet<string>();

            // 같은 조회에 미확정으로 실려 온 상품. 확정 목록에 없지만 지금 지급·확정 중이거나 막 끝났다 —
            // 회수하면 방금 기록한 소유를 지우게 된다.
            var pending = new HashSet<string>();

            // 앱이 죽어 확정하지 못했던 구매들. 지급되지 않았을 수 있으므로 다시 흘려보낸다.
            foreach (var order in orders.PendingOrders)
            {
                var pendingId = ProductOf(order)?.definition?.id;
                if (!string.IsNullOrEmpty(pendingId)) pending.Add(pendingId);

                var transactionId = order.Info?.TransactionID;

                // 이번 세션에 확정했지만 스토어에 아직 반영되지 않은 주문 — 지급은 끝났으니 소유로 센다.
                if (!string.IsNullOrEmpty(transactionId) && _confirmedInSession.Contains(transactionId))
                {
                    var productId = ProductOf(order)?.definition?.id;
                    if (!string.IsNullOrEmpty(productId)) owned.Add(productId);
                    continue;
                }

                _ = ProcessPendingAsync(order, isRestored: true);
            }

            foreach (var order in orders.ConfirmedOrders)
            {
                var productId = ProductOf(order)?.definition?.id;
                if (!string.IsNullOrEmpty(productId)) owned.Add(productId);
            }

            SyncOwnership(owned, pending);

            _fetchingPurchases?.TrySetResult(true);
            CompleteRestoreFetch(true);
            FinishRefetch();
        }

        // 스토어의 확정 목록이 진실이다. 목록에 있으면 소유(재설치·기기 변경), 없으면 회수(환불)한다.
        // 이미 확정·지급된 구매이므로 지급 파이프라인을 다시 타지 않는다.
        private void SyncOwnership(HashSet<string> owned, HashSet<string> pending)
        {
            foreach (var definition in _catalog)
            {
                if (definition.type != ProductType.NonConsumable) continue;

                var isOwned = owned.Contains(definition.id);
                if (isOwned && _restoring != null) _restoredCount++;

                if (_entitlements.IsOwned(definition.id) == isOwned) continue;
                if (!isOwned && pending.Contains(definition.id)) continue;

                _entitlements.SetOwned(definition.id, isOwned);
                OwnedChanged?.Invoke(definition.id);
            }
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[IAPService] 이전 구매 조회에 실패했다: {failure?.message}");

            _fetchingPurchases?.TrySetResult(false);
            CompleteRestoreFetch(false);
            FinishRefetch();
        }

        private void CompleteRestoreFetch(bool success)
        {
            if (_restoring == null) return;

            if (_restoreCallbackSucceeded) FinishRestore(success);
            else _restoreFetchResult = success;
        }

        private void FinishRestore(bool success)
        {
            var source = _restoring;
            _restoring = null;
            _restoreCallbackSucceeded = false;
            _restoreFetchResult = null;

            source?.TrySetResult(success);

            // 복원이 조회 없이 끝났다(콜백 실패). 그 조회에 편승한 재처리 대기를 풀어 준다.
            if (!_refetching) CompleteRefetchWaiters();
        }

        private void FinishRefetch()
        {
            _refetching = false;
            CompleteRefetchWaiters();
        }

        private void CompleteRefetchWaiters()
        {
            if (_refetchWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource>(_refetchWaiters);
            _refetchWaiters.Clear();

            foreach (var waiter in waiters) waiter.TrySetResult();
        }

        private void HandlePurchasePending(PendingOrder order) => _ = ProcessPendingAsync(order, isRestored: false);

        // 확정 결과. 실패해도 할 일은 없다 — 스토어가 다음 실행에 같은 구매를 미확정으로 다시 주고,
        // 그때 지급 핸들러가 TransactionId로 중복 지급을 막는다. 다만 조용히 넘기면 발견이 늦다.
        private void HandlePurchaseConfirmed(Order order)
        {
            if (order is not FailedOrder failed) return;

            Debug.LogWarning($"[IAPService] 구매 확정에 실패했다: {ProductOf(failed)?.definition?.id} " +
                             $"{failed.FailureReason} {failed.Details}");
        }

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

            // 같은 소모성 상품의 미확정 구매가 새 구매를 막고 있다(구글 ItemAlreadyOwned). 그 구매를 지금
            // 처리해야 막힘이 풀린다 — 다음 실행까지 기다리면 그동안 이 상품을 살 수 없다.
            if (order.FailureReason == PurchaseFailureReason.DuplicateTransaction &&
                ProductOf(order)?.definition?.type == ProductType.Consumable)
            {
                _ = ProcessUnconfirmedAsync();
            }
        }

        // 주문 이벤트 핸들러에서 기다리지 않고 부른다 — 예외는 전부 안에서 잡는다.
        private async Awaitable ProcessPendingAsync(PendingOrder order, bool isRestored)
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

            // 이미 확정했다 — 보류 목록과 조회가 같은 주문을 함께 실어 와도 두 번 지급하지 않는다.
            if (_confirmedInSession.Contains(transactionId)) return;
            if (_inFlight.Contains(transactionId)) return;

            if (!IsFulfillmentReady())
            {
                Hold(order, isRestored);
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                    new IAPError(IAPErrorCodes.FulfillmentNotReady, "지급 처리기가 준비되지 않아 보류했다")));
                return;
            }

            _inFlight.Add(transactionId);

            try
            {
                await FulfillAndConfirmAsync(order, product, isRestored);
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAPService] 구매를 처리하다 예외가 났다. 확정하지 않는다: {productId} {e}");
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                    new IAPError(IAPErrorCodes.FulfillmentFailed, e.Message)));
            }
            finally
            {
                _inFlight.Remove(transactionId);
            }
        }

        private bool IsFulfillmentReady()
        {
            if (_fulfillment is not IIAPFulfillmentGate gate) return true;

            try
            {
                return gate.IsReady;
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAPService] 지급 처리기의 IsReady가 예외를 던졌다. 준비되지 않은 것으로 본다: {e}");
                return false;
            }
        }

        private void Hold(PendingOrder order, bool isRestored)
        {
            var transactionId = order.Info.TransactionID;

            foreach (var entry in _held)
            {
                if (entry.Order.Info?.TransactionID == transactionId) return;
            }

            _held.Add(new HeldOrder(order, isRestored));

            if (_verboseLogging)
            {
                Debug.Log($"[IAPService] 지급 처리기가 준비되지 않아 구매를 보류했다: {ProductOf(order)?.definition?.id} {transactionId}");
            }
        }

        private void ReleaseHeld()
        {
            if (_held.Count == 0 || !IsFulfillmentReady()) return;

            var held = new List<HeldOrder>(_held);
            _held.Clear();

            foreach (var entry in held) _ = ProcessPendingAsync(entry.Order, entry.IsRestored);
        }

        // 소모성을 지급 없이 확정하면 결제만 되고 재화가 없다. 등록을 빠뜨려도 조용히 동작해 늦게 발견된다.
        private void WarnIfConsumablesAutoConfirmed()
        {
            if (_fulfillment is not AutoConfirmFulfillment) return;

            foreach (var definition in _catalog)
            {
                if (definition.type != ProductType.Consumable) continue;

                Debug.LogWarning("[IAPService] 소모성 상품이 있는데 지급 처리기(IIAPFulfillment)가 등록되지 않았다. " +
                                 "구매가 지급 없이 확정된다.");
                return;
            }
        }

        private async Awaitable FulfillAndConfirmAsync(PendingOrder order, Product product, bool isRestored)
        {
            var productId = product.definition.id;

            var purchase = BuildPurchase(product, order, isRestored);

            if (!_validator.Validate(purchase, out var validationError))
            {
                Debug.LogError($"[IAPService] 영수증 검증에 실패했다: {purchase.ProductId} {validationError}");
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.InvalidReceipt(validationError));
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

            // 지급을 기다리는 사이 해제됐다. 확정하지 않으면 스토어가 다음 실행에 다시 준다.
            if (_disposed) return;

            if (!fulfilled)
            {
                // 확정하지 않는다 — 스토어가 다음 실행에 같은 구매를 다시 내려준다.
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                    new IAPError(IAPErrorCodes.FulfillmentFailed, "지급에 실패해 확정하지 않았다")));
                return;
            }

            _confirmedInSession.Add(order.Info.TransactionID);
            _purchaseService.ConfirmPurchase(order);

            if (purchase.Type == IAPProductType.NonConsumable && !_entitlements.IsOwned(productId))
            {
                _entitlements.SetOwned(productId, true);
                OwnedChanged?.Invoke(productId);
            }

            if (isRestored) _restoredCount++;

            Purchased?.Invoke(purchase);

            CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Purchased(purchase));
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

            CompletePending(result);
        }

        // 재전달된 주문(isRestored)의 결과로는 진행 중인 구매를 끝내지 않는다. 주문 이벤트에는 "어느 호출의
        // 결과인가"가 없어 상품 ID로만 맞추는데, 같은 상품의 예전 미확정 구매가 그 사이 처리되면 지금 진행 중인
        // 구매가 남의 결과로 끝나고 진짜 새 구매는 기다리는 호출자 없이 처리된다.
        private void CompleteOwnPurchase(string productId, bool isRestored, IAPPurchaseResult result)
        {
            if (isRestored) return;

            CompletePendingFor(productId, result);
        }

        private void CompletePending(IAPPurchaseResult result)
        {
            var source = _pendingSource;
            if (source == null) return;

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

        private static Awaitable CompletedAwaitable()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        private readonly struct HeldOrder
        {
            public readonly PendingOrder Order;
            public readonly bool IsRestored;

            public HeldOrder(PendingOrder order, bool isRestored)
            {
                Order = order;
                IsRestored = isRestored;
            }
        }
    }
}
