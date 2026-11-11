using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Purchasing;

namespace DarkNaku.FoundationDI
{
    // 원스토어 인앱 결제(SDK v21)를 쓰는 IIAPService 구현. 규율은 IAPService와 같다 — 검증 → 지급 → 확정,
    // 지급 처리기 준비 전 보류, 이중 지급 방지, 스토어 목록으로 소유 동기화.
    //
    // 원스토어의 확정은 상품 종류로 갈린다. 소모성은 소비(Consume)하면 구매 조회에서 빠지고, 비소모성은
    // 승인(Acknowledge)하면 조회에 승인된 채 남는다 — 승인된 비소모성이 곧 소유 목록이고, 승인하지 않은
    // 구매와 소비하지 않은 소모성이 미확정 구매다. 별도의 복원 API가 없어 복원도 구매 조회다.
    public sealed class OneStoreIAPService : IIAPService
    {
        // IAP Catalog의 스토어별 ID 키. 비어 있으면 공용 ID를 쓴다.
        public const string StoreName = "OneStore";

        private readonly IOneStoreClient _client;
        private readonly List<ProductDefinition> _catalog;
        private readonly IIAPFulfillment _fulfillment;
        private readonly IReceiptValidator _validator;
        private readonly IAsyncReceiptValidator _asyncValidator;
        private readonly IEntitlementStorage _entitlements;
        private readonly IAPServiceOptions _options;
        private readonly Func<IMainThreadDispatcher> _dispatcherFactory;
        private IMainThreadDispatcher _dispatcher;
        private readonly bool _verboseLogging;

        private readonly List<IAPProduct> _products = new();

        private readonly List<AwaitableCompletionSource<bool>> _initWaiters = new();
        private bool _initializing;

        // 대기 중인 PurchaseAsync. 원스토어 결제 화면도 모달이라 하나만 허용한다.
        private AwaitableCompletionSource<IAPPurchaseResult> _pendingSource;
        private string _pendingProductId;

        // 지급이 진행 중인 구매와 이번 세션에 확정한 구매(트랜잭션 ID). 확정은 스토어에 비동기로 반영되므로
        // 직후의 조회가 같은 구매를 아직 미확정으로 돌려줄 수 있다.
        private readonly HashSet<string> _inFlight = new();
        private readonly HashSet<string> _confirmedInSession = new();

        private readonly List<HeldPurchase> _held = new();
        private readonly HashSet<string> _invalidLogged = new();

        // 구매 조회는 하나만 진행하고, 초기화·복원·재처리가 그 결과를 함께 기다린다.
        private readonly List<AwaitableCompletionSource<bool>> _queryWaiters = new();
        private bool _querying;

        private bool _restoring;
        private int _restoredCount;

        private bool _subscribed;
        private bool _disposed;

        internal OneStoreIAPService(IOneStoreClient client, IReadOnlyList<ProductDefinition> catalog,
                                    IAPServiceDependencies dependencies)
        {
            dependencies ??= new IAPServiceDependencies();

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _catalog = catalog != null ? new List<ProductDefinition>(catalog) : new List<ProductDefinition>();
            _fulfillment = dependencies.Fulfillment ?? new AutoConfirmFulfillment();
            _validator = dependencies.Validator ?? new OneStoreReceiptValidator();
            _asyncValidator = dependencies.AsyncValidator;
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

            if (!TryGetProduct(productId, out var product))
            {
                Debug.LogWarning($"[OneStoreIAPService] 카탈로그에 없는 상품을 구매하려 했다: {productId}");
                return IAPPurchaseResult.NotReady();
            }

            if (product.Type == IAPProductType.NonConsumable && IsOwned(productId))
            {
                return IAPPurchaseResult.AlreadyOwned(OwnedPurchaseOf(product));
            }

            if (_pendingSource != null) return IAPPurchaseResult.NotReady();

            var source = new AwaitableCompletionSource<IAPPurchaseResult>();
            _pendingSource = source;
            _pendingProductId = productId;

            _client.Purchase(product.StoreId, (result, purchases) => HandlePurchaseResult(productId, result, purchases));

            var seconds = _options.PurchaseTimeoutSeconds;

            using var timeout = StartTimeout(seconds, () =>
            {
                if (_pendingSource != source) return;

                Debug.LogWarning($"[OneStoreIAPService] 구매 결과가 {seconds}초 안에 오지 않았다: {productId}. " +
                                 "결제가 끝났다면 늦게라도 지급된다.");
                CompletePending(IAPPurchaseResult.Failed(new IAPError(IAPErrorCodes.Timeout, "구매 결과를 기다리다 시간이 초과됐다")));
            });

            return await source.Awaitable;
        }

        // 원스토어에는 복원 API가 없다. 구매 조회가 승인된 비소모성을 돌려주므로 그걸로 소유를 맞추고,
        // 같은 조회에 실려 온 미확정 구매를 지급한다.
        public async Awaitable<IAPRestoreResult> RestoreAsync()
        {
            if (_disposed || !IsInitialized)
            {
                return IAPRestoreResult.Fail(new IAPError(IAPErrorCodes.NotInitialized, "초기화되지 않았다"));
            }

            if (_restoring)
            {
                Debug.LogWarning("[OneStoreIAPService] 복원이 이미 진행 중이다.");
                return IAPRestoreResult.Fail(new IAPError(IAPErrorCodes.RestoreInProgress, "복원이 이미 진행 중이다"));
            }

            _restoring = true;
            _restoredCount = 0;

            var success = await QueryPurchasesAsync();

            _restoring = false;

            return success
                ? IAPRestoreResult.Ok(_restoredCount)
                : IAPRestoreResult.Fail(new IAPError(IAPErrorCodes.RestoreFailed, "복원에 실패했다"));
        }

        public async Awaitable ProcessUnconfirmedAsync()
        {
            if (_disposed || !IsInitialized) return;

            ReleaseHeld();

            await QueryPurchasesAsync();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Unsubscribe();

            CompletePending(IAPPurchaseResult.Failed(new IAPError(IAPErrorCodes.Disposed, "서비스가 해제됐다")));
            CompleteInitWaiters(false);
            CompleteQueryWaiters(false);

            _held.Clear();
            _client.Dispose();
        }

        private async Awaitable<bool> RunInitializeAsync()
        {
            WarnIfConsumablesAutoConfirmed();
            Subscribe();

            var success = await QueryProductsAsync();

            if (success)
            {
                // 실패해도 치명적이지 않다 — 미확정 구매를 이번에 못 찾을 뿐이고 다음 조회에 다시 온다.
                await WithTimeout(QueryPurchasesAsync(), _options.FetchTimeoutSeconds, "구매 조회");
            }
            else
            {
                Debug.LogError("[OneStoreIAPService] 원스토어 결제 초기화에 실패했다.");
            }

            IsInitialized = success && !_disposed;
            _initializing = false;

            CompleteInitWaiters(IsInitialized);
            return IsInitialized;
        }

        // SDK는 첫 요청에서 연결하므로 연결과 상품 조회가 한 요청이다. 두 시간 제한을 더해 기다린다.
        private Awaitable<bool> QueryProductsAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            var storeIds = new List<string>();

            foreach (var definition in _catalog) storeIds.Add(definition.storeSpecificId);

            _client.QueryProducts(storeIds, (result, products) =>
            {
                if (_disposed) return;

                source.TrySetResult(HandleProductsQueried(result, products));
            });

            var seconds = SumTimeout(_options.ConnectTimeoutSeconds, _options.FetchTimeoutSeconds);
            return WithTimeout(source.Awaitable, seconds, "연결·상품 조회");
        }

        private bool HandleProductsQueried(OneStoreResult result, IReadOnlyList<OneStoreProduct> products)
        {
            _products.Clear();

            if (!result.IsSuccess)
            {
                Debug.LogError($"[OneStoreIAPService] 상품 조회에 실패했다: {result}");
                return false;
            }

            var missing = new List<string>();

            foreach (var definition in _catalog)
            {
                var detail = FindProduct(products, definition.storeSpecificId);

                if (detail == null)
                {
                    missing.Add(definition.id);
                    continue;
                }

                _products.Add(ToIAPProduct(definition, detail));
            }

            if (_products.Count == 0)
            {
                Debug.LogError("[OneStoreIAPService] 상품을 하나도 조회하지 못했다. 원스토어 개발자센터의 상품 ID를 확인할 것.");
                return false;
            }

            // 개발자센터에 없는(심사 전·삭제된) 상품 하나 때문에 상점 전체를 닫지 않는다.
            if (missing.Count > 0)
            {
                Debug.LogWarning($"[OneStoreIAPService] 일부 상품을 조회하지 못해 빼고 진행한다(개발자센터에 없거나 판매 중지): " +
                                 string.Join(", ", missing));
            }

            if (_verboseLogging) Debug.Log($"[OneStoreIAPService] 상품 {_products.Count}개를 조회했다.");

            return true;
        }

        private Awaitable<bool> QueryPurchasesAsync()
        {
            var waiter = new AwaitableCompletionSource<bool>();

            if (_disposed)
            {
                waiter.SetResult(false);
                return waiter.Awaitable;
            }

            _queryWaiters.Add(waiter);

            if (!_querying)
            {
                _querying = true;
                _client.QueryPurchases(HandlePurchasesQueried);
            }

            return waiter.Awaitable;
        }

        private void HandlePurchasesQueried(OneStoreResult result, IReadOnlyList<OneStorePurchase> purchases)
        {
            if (_disposed) return;

            _querying = false;

            if (!result.IsSuccess)
            {
                Debug.LogWarning($"[OneStoreIAPService] 구매 조회에 실패했다: {result}");
                CompleteQueryWaiters(false);
                return;
            }

            var owned = new HashSet<string>();

            // 같은 조회에 미확정으로 실려 온 상품. 지금 지급·확정 중일 수 있어 회수하지 않는다.
            var pending = new HashSet<string>();

            foreach (var purchase in purchases ?? Array.Empty<OneStorePurchase>())
            {
                var definition = DefinitionOfStoreId(purchase?.ProductId);
                if (definition == null || purchase.PurchaseState != OneStorePurchaseStates.Purchased) continue;

                var isNonConsumable = definition.type == ProductType.NonConsumable;

                if (isNonConsumable && purchase.IsAcknowledged)
                {
                    owned.Add(definition.id);
                    continue;
                }

                // 이번 세션에 확정했지만 스토어에 아직 반영되지 않은 구매 — 지급은 끝났다.
                if (_confirmedInSession.Contains(purchase.TransactionId))
                {
                    if (isNonConsumable) owned.Add(definition.id);
                    continue;
                }

                pending.Add(definition.id);
                _ = ProcessPurchaseAsync(purchase, isRestored: true);
            }

            SyncOwnership(owned, pending);
            CompleteQueryWaiters(true);
        }

        // 스토어의 승인 목록이 진실이다. 있으면 소유(재설치·기기 변경), 없으면 회수(환불)한다.
        private void SyncOwnership(HashSet<string> owned, HashSet<string> pending)
        {
            foreach (var definition in _catalog)
            {
                if (definition.type != ProductType.NonConsumable) continue;

                var isOwned = owned.Contains(definition.id);
                if (isOwned && _restoring) _restoredCount++;

                if (_entitlements.IsOwned(definition.id) == isOwned) continue;
                if (!isOwned && pending.Contains(definition.id)) continue;

                _entitlements.SetOwned(definition.id, isOwned);
                OwnedChanged?.Invoke(definition.id);
            }
        }

        private void HandlePurchaseResult(string productId, OneStoreResult result, IReadOnlyList<OneStorePurchase> purchases)
        {
            if (_disposed) return;

            if (result.IsSuccess)
            {
                var arrived = false;

                foreach (var purchase in purchases ?? Array.Empty<OneStorePurchase>())
                {
                    if (DefinitionOfStoreId(purchase?.ProductId)?.id == productId) arrived = true;

                    _ = ProcessPurchaseAsync(purchase, isRestored: false);
                }

                if (!arrived)
                {
                    Debug.LogWarning($"[OneStoreIAPService] 구매가 성공했다는데 그 상품의 구매가 오지 않았다: {productId}");
                    CompletePendingFor(productId, IAPPurchaseResult.Failed(
                        new IAPError(OneStoreResponseCodes.NoResponse, "구매 결과에 상품이 없다")));
                }

                return;
            }

            // 취소는 에러가 아니다.
            if (result.Code == OneStoreResponseCodes.UserCanceled)
            {
                CompletePendingFor(productId, IAPPurchaseResult.UserCancelled());
                return;
            }

            // 확정하지 않은 같은 상품의 구매가 새 구매를 막고 있다. Unity IAP 구현과 같은 코드로 알리고
            // 그 구매를 지금 처리한다 — 다음 실행까지 기다리면 그동안 이 상품을 살 수 없다.
            var alreadyOwned = result.Code == OneStoreResponseCodes.ItemAlreadyOwned;
            var error = new IAPError(alreadyOwned ? IAPErrorCodes.DuplicateTransaction : result.Code,
                                     result.Message ?? $"원스토어 응답 코드 {result.Code}");

            Debug.LogWarning($"[OneStoreIAPService] 구매에 실패했다: {productId} {error}");
            CompletePendingFor(productId, IAPPurchaseResult.Failed(error));

            if (alreadyOwned) _ = ProcessUnconfirmedAsync();
        }

        private void HandlePurchasesUpdated(IReadOnlyList<OneStorePurchase> purchases)
        {
            if (_disposed) return;

            foreach (var purchase in purchases ?? Array.Empty<OneStorePurchase>())
            {
                _ = ProcessPurchaseAsync(purchase, isRestored: false);
            }
        }

        // 구매 이벤트에서 기다리지 않고 부른다 — 예외는 전부 안에서 잡는다.
        private async Awaitable ProcessPurchaseAsync(OneStorePurchase purchase, bool isRestored)
        {
            var definition = DefinitionOfStoreId(purchase?.ProductId);

            if (definition == null)
            {
                // 확정하면 영영 지급할 수 없다. 다음 버전의 카탈로그가 알 수도 있으므로 남긴다.
                Debug.LogWarning($"[OneStoreIAPService] 카탈로그에 없는 상품의 구매가 도착했다: {purchase?.ProductId}. " +
                                 "확정하지 않고 남겨둔다.");
                return;
            }

            var productId = definition.id;

            if (purchase.PurchaseState != OneStorePurchaseStates.Purchased)
            {
                if (_verboseLogging)
                {
                    Debug.Log($"[OneStoreIAPService] 결제 완료 상태가 아닌 구매를 건너뛴다: {productId} {purchase.PurchaseState}");
                }

                return;
            }

            var transactionId = purchase.TransactionId;

            if (string.IsNullOrEmpty(transactionId))
            {
                Debug.LogWarning($"[OneStoreIAPService] 주문 ID가 없는 구매가 도착했다: {productId}. 확정할 수 없어 건너뛴다.");
                return;
            }

            if (_confirmedInSession.Contains(transactionId)) return;
            if (_inFlight.Contains(transactionId)) return;

            if (!IsFulfillmentReady())
            {
                Hold(purchase, isRestored);
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                    new IAPError(IAPErrorCodes.FulfillmentNotReady, "지급 처리기가 준비되지 않아 보류했다")));
                return;
            }

            _inFlight.Add(transactionId);

            try
            {
                await FulfillAndConfirmAsync(purchase, definition, isRestored);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OneStoreIAPService] 구매를 처리하다 예외가 났다. 확정하지 않는다: {productId} {e}");
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                    new IAPError(IAPErrorCodes.FulfillmentFailed, e.Message)));
            }
            finally
            {
                _inFlight.Remove(transactionId);
            }
        }

        private async Awaitable FulfillAndConfirmAsync(OneStorePurchase storePurchase, ProductDefinition definition,
                                                       bool isRestored)
        {
            var productId = definition.id;
            var purchase = BuildPurchase(storePurchase, definition, isRestored);

            if (!_validator.Validate(purchase, out var validationError))
            {
                RejectInvalidReceipt(storePurchase, purchase, isRestored, validationError);
                return;
            }

            if (_asyncValidator != null)
            {
                var validation = await ValidateOnServerAsync(purchase);

                if (_disposed) return;

                if (validation.Status == IAPValidationStatus.Invalid)
                {
                    RejectInvalidReceipt(storePurchase, purchase, isRestored,
                                         new IAPError(IAPErrorCodes.ReceiptRejected, validation.Message));
                    return;
                }

                if (validation.Status == IAPValidationStatus.RetryLater)
                {
                    Debug.LogWarning($"[OneStoreIAPService] 서버 검증을 나중에 다시 한다. 확정하지 않는다: {productId} {validation.Message}");
                    CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                        new IAPError(IAPErrorCodes.ValidationRetryLater, validation.Message)));
                    return;
                }
            }

            bool fulfilled;

            try
            {
                fulfilled = await _fulfillment.FulfillAsync(purchase);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OneStoreIAPService] 지급 핸들러가 예외를 던졌다. 확정하지 않는다: {e}");
                fulfilled = false;
            }

            if (_disposed) return;

            if (!fulfilled)
            {
                CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Failed(
                    new IAPError(IAPErrorCodes.FulfillmentFailed, "지급에 실패해 확정하지 않았다")));
                return;
            }

            Confirm(storePurchase, definition);

            if (purchase.Type == IAPProductType.NonConsumable && !_entitlements.IsOwned(productId))
            {
                _entitlements.SetOwned(productId, true);
                OwnedChanged?.Invoke(productId);
            }

            if (isRestored && _restoring) _restoredCount++;

            Purchased?.Invoke(purchase);

            CompleteOwnPurchase(productId, isRestored, IAPPurchaseResult.Purchased(purchase));
        }

        // 확정 실패에 할 일은 없다 — 스토어가 다음 조회에 같은 구매를 미확정으로 다시 주고, 지급 처리기가
        // 트랜잭션 ID로 중복 지급을 막는다. 다만 조용히 넘기면 발견이 늦다.
        private void Confirm(OneStorePurchase purchase, ProductDefinition definition)
        {
            _confirmedInSession.Add(purchase.TransactionId);

            Action<OneStoreResult> onDone = result =>
            {
                if (result.IsSuccess) return;

                Debug.LogWarning($"[OneStoreIAPService] 구매 확정에 실패했다: {definition.id} {result}");
            };

            if (definition.type == ProductType.NonConsumable) _client.Acknowledge(purchase, onDone);
            else _client.Consume(purchase, onDone);
        }

        private async Awaitable<IAPValidationResult> ValidateOnServerAsync(IAPPurchase purchase)
        {
            try
            {
                return await _asyncValidator.ValidateAsync(purchase);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OneStoreIAPService] 서버 검증기가 예외를 던졌다. 나중에 다시 검증한다: {e}");
                return IAPValidationResult.RetryLater(e.Message);
            }
        }

        private void RejectInvalidReceipt(OneStorePurchase storePurchase, IAPPurchase purchase, bool isRestored,
                                          IAPError error)
        {
            if (_invalidLogged.Add(storePurchase.TransactionId))
            {
                Debug.LogError($"[OneStoreIAPService] 영수증 검증에 실패했다: {purchase.ProductId} {error}");
            }

            if (_options.InvalidReceiptPolicy == InvalidReceiptPolicy.ConfirmWithoutGrant &&
                purchase.Type == IAPProductType.Consumable)
            {
                _confirmedInSession.Add(storePurchase.TransactionId);
                _client.Consume(storePurchase, _ => { });
            }

            CompleteOwnPurchase(purchase.ProductId, isRestored, IAPPurchaseResult.InvalidReceipt(error));
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
                Debug.LogError($"[OneStoreIAPService] 지급 처리기의 IsReady가 예외를 던졌다. 준비되지 않은 것으로 본다: {e}");
                return false;
            }
        }

        private void Hold(OneStorePurchase purchase, bool isRestored)
        {
            foreach (var entry in _held)
            {
                if (entry.Purchase.TransactionId == purchase.TransactionId) return;
            }

            _held.Add(new HeldPurchase(purchase, isRestored));

            if (_verboseLogging)
            {
                Debug.Log($"[OneStoreIAPService] 지급 처리기가 준비되지 않아 구매를 보류했다: {purchase.ProductId} {purchase.TransactionId}");
            }
        }

        private void ReleaseHeld()
        {
            if (_held.Count == 0 || !IsFulfillmentReady()) return;

            var held = new List<HeldPurchase>(_held);
            _held.Clear();

            foreach (var entry in held) _ = ProcessPurchaseAsync(entry.Purchase, entry.IsRestored);
        }

        private void WarnIfConsumablesAutoConfirmed()
        {
            if (_fulfillment is not AutoConfirmFulfillment) return;

            foreach (var definition in _catalog)
            {
                if (definition.type != ProductType.Consumable) continue;

                Debug.LogWarning("[OneStoreIAPService] 소모성 상품이 있는데 지급 처리기(IIAPFulfillment)가 등록되지 않았다. " +
                                 "구매가 지급 없이 확정된다.");
                return;
            }
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            _client.PurchasesUpdated += HandlePurchasesUpdated;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            _client.PurchasesUpdated -= HandlePurchasesUpdated;
            _subscribed = false;
        }

        // 늦게 온 응답은 그대로 처리된다 — 기다리는 쪽만 먼저 끝낸다.
        private async Awaitable<bool> WithTimeout(Awaitable<bool> awaitable, float seconds, string what)
        {
            var source = new AwaitableCompletionSource<bool>();

            using var timeout = StartTimeout(seconds, () =>
            {
                Debug.LogWarning($"[OneStoreIAPService] {what}이 {seconds}초 안에 끝나지 않았다.");
                source.TrySetResult(false);
            });

            _ = Forward(awaitable, source);

            return await source.Awaitable;
        }

        private static async Awaitable Forward(Awaitable<bool> awaitable, AwaitableCompletionSource<bool> source)
        {
            source.TrySetResult(await awaitable);
        }

        private IDisposable StartTimeout(float seconds, Action onTimeout)
        {
            if (seconds <= 0f || _disposed) return null;

            _dispatcher ??= _dispatcherFactory?.Invoke();

            return _dispatcher?.Delay(seconds, onTimeout);
        }

        // 둘 중 하나라도 0 이하(기다림)면 기다린다.
        private static float SumTimeout(float a, float b) => a <= 0f || b <= 0f ? 0f : a + b;

        private void CompleteInitWaiters(bool success)
        {
            if (_initWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<bool>>(_initWaiters);
            _initWaiters.Clear();

            foreach (var waiter in waiters) waiter.TrySetResult(success);
        }

        private void CompleteQueryWaiters(bool success)
        {
            _querying = false;

            if (_queryWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<bool>>(_queryWaiters);
            _queryWaiters.Clear();

            foreach (var waiter in waiters) waiter.TrySetResult(success);
        }

        private void CompletePendingFor(string productId, IAPPurchaseResult result)
        {
            if (_pendingSource == null || _pendingProductId != productId) return;

            CompletePending(result);
        }

        // 재전달된 구매(isRestored)의 결과로는 진행 중인 구매를 끝내지 않는다(IAPService와 같은 이유).
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

        private ProductDefinition DefinitionOfStoreId(string storeId)
        {
            if (string.IsNullOrEmpty(storeId)) return null;

            foreach (var definition in _catalog)
            {
                if (definition.storeSpecificId == storeId) return definition;
            }

            return null;
        }

        private IAPPurchase BuildPurchase(OneStorePurchase storePurchase, ProductDefinition definition, bool isRestored)
        {
            TryGetProduct(definition.id, out var product);

            return new IAPPurchase(definition.id, ToProductType(definition.type), storePurchase.TransactionId,
                                   storePurchase.Receipt, product.Price, product.CurrencyCode ?? string.Empty,
                                   isRestored);
        }

        private static IAPPurchase OwnedPurchaseOf(IAPProduct product) =>
            new(product.Id, product.Type, null, null, product.Price, product.CurrencyCode ?? string.Empty, true);

        private static OneStoreProduct FindProduct(IReadOnlyList<OneStoreProduct> products, string storeId)
        {
            if (products == null) return null;

            foreach (var product in products)
            {
                if (product != null && product.ProductId == storeId) return product;
            }

            return null;
        }

        private static IAPProduct ToIAPProduct(ProductDefinition definition, OneStoreProduct detail)
        {
            var price = PriceOf(detail);
            var currency = string.IsNullOrEmpty(detail.PriceCurrencyCode) ? "KRW" : detail.PriceCurrencyCode;

            return new IAPProduct(definition.id, definition.storeSpecificId, ToProductType(definition.type),
                                  detail.Title, string.Empty, FormatPrice(detail.Price, price, currency),
                                  price, currency, true);
        }

        private static double PriceOf(OneStoreProduct detail)
        {
            if (detail.PriceAmountMicros > 0) return detail.PriceAmountMicros / 1_000_000.0;

            return double.TryParse(detail.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                ? price
                : 0.0;
        }

        // 원스토어 가격은 통화 기호 없는 숫자다("1200"). 원화는 "₩1,200"으로, 그 밖은 "1.99 USD"로 찍는다.
        // 이미 기호가 붙어 오면 그대로 쓴다.
        internal static string FormatPrice(string raw, double price, string currencyCode)
        {
            if (!string.IsNullOrEmpty(raw) && !IsPlainNumber(raw)) return raw;

            return currencyCode == "KRW"
                ? "₩" + price.ToString("N0", CultureInfo.InvariantCulture)
                : price.ToString("0.##", CultureInfo.InvariantCulture) + " " + currencyCode;
        }

        private static bool IsPlainNumber(string value)
        {
            foreach (var c in value)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',') return false;
            }

            return true;
        }

        private static IAPProductType ToProductType(ProductType type) =>
            type == ProductType.NonConsumable ? IAPProductType.NonConsumable : IAPProductType.Consumable;

        private static Awaitable<T> Completed<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private readonly struct HeldPurchase
        {
            public readonly OneStorePurchase Purchase;
            public readonly bool IsRestored;

            public HeldPurchase(OneStorePurchase purchase, bool isRestored)
            {
                Purchase = purchase;
                IsRestored = isRestored;
            }
        }
    }
}
