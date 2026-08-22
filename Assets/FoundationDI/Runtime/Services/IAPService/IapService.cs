using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 정책 계층. provider가 무엇이든 여기서 검증 → 지급 → 확정 → 소유 기록 → 이벤트 순서가 지켜진다.
    public sealed class IapService : IIapService
    {
        // 지급 실패는 스토어 실패와 구분되는 원인이지만 결과 enum을 늘리는 대신 에러 코드로 남긴다 —
        // 호출부가 분기할 이유가 없다(둘 다 "이번엔 못 줬다"이고 재시도는 스토어가 알아서 한다).
        internal const int FulfillmentFailedCode = -1001;
        internal const int PurchaseStartFailedCode = -1002;
        internal const int DisposedCode = -1003;

        private readonly IIapProvider _provider;
        private readonly IIapFulfillment _fulfillment;
        private readonly IReceiptValidator _validator;
        private readonly IEntitlementStorage _entitlements;
        private readonly bool _verboseLogging;

        // 공용 ID → 정의, 스토어 ID → 정의. 둘 다 필요하다 — 게임은 공용 ID로 부르고
        // provider는 스토어 ID로 답하기 때문이다.
        private readonly Dictionary<string, IapProductDefinition> _byId = new();
        private readonly Dictionary<string, IapProductDefinition> _byStoreId = new();

        // 진행 중인 초기화에 편승한 호출자들. Awaitable은 단일 사용이라 하나를 여럿이 await 할 수
        // 없으므로, 호출자마다 별도의 완료 소스를 만들어 두었다가 한꺼번에 같은 결과로 완료시킨다.
        private readonly List<AwaitableCompletionSource<bool>> _initWaiters = new();
        private bool _initializing;

        // 대기 중인 PurchaseAsync. 스토어 UI가 모달이라 동시에 두 개가 뜨는 일은 없고,
        // provider 이벤트에는 "어느 호출의 결과인가"가 실려 오지 않으므로 하나만 허용한다.
        private AwaitableCompletionSource<IapPurchaseResult> _pendingSource;
        private string _pendingProductId;

        private bool _subscribed;
        private bool _disposed;

        public IapService(IIapProvider provider, IapServiceOptions options,
                          IIapFulfillment fulfillment = null,
                          IReceiptValidator validator = null,
                          IEntitlementStorage entitlements = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _fulfillment = fulfillment ?? new AutoConfirmFulfillment();
            _validator = validator ?? new NoopReceiptValidator();
            _entitlements = entitlements ?? new PlayerPrefsEntitlementStorage();
            _verboseLogging = options.VerboseLogging;

            if (options.Products != null)
            {
                foreach (var definition in options.Products)
                {
                    if (string.IsNullOrEmpty(definition.Id)) continue;

                    _byId[definition.Id] = definition;
                    if (!string.IsNullOrEmpty(definition.StoreId)) _byStoreId[definition.StoreId] = definition;
                }
            }
        }

        public bool IsInitialized { get; private set; }

        public IReadOnlyList<IapProduct> Products =>
            IsInitialized ? _provider.Products : Array.Empty<IapProduct>();

        public event Action<IapPurchase> Purchased;
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

        public bool TryGetProduct(string productId, out IapProduct product)
        {
            product = default;

            if (!IsInitialized || string.IsNullOrEmpty(productId)) return false;

            foreach (var candidate in _provider.Products)
            {
                if (candidate.Id != productId) continue;

                product = candidate;
                return true;
            }

            return false;
        }

        public bool IsOwned(string productId) =>
            !string.IsNullOrEmpty(productId) && _entitlements.IsOwned(productId);

        public async Awaitable<IapPurchaseResult> PurchaseAsync(string productId)
        {
            if (_disposed || !IsInitialized) return IapPurchaseResult.NotReady();

            if (string.IsNullOrEmpty(productId) || !_byId.TryGetValue(productId, out var definition))
            {
                Debug.LogWarning($"[IAPService] 카탈로그에 없는 상품을 구매하려 했다: {productId}");
                return IapPurchaseResult.NotReady();
            }

            _pendingSource = new AwaitableCompletionSource<IapPurchaseResult>();
            _pendingProductId = productId;

            if (!_provider.Purchase(definition.StoreId))
            {
                ClearPending();
                return IapPurchaseResult.Failed(
                    new IapError(PurchaseStartFailedCode, "구매를 시작하지 못했다"));
            }

            return await _pendingSource.Awaitable;
        }

        public Awaitable<IapRestoreResult> RestoreAsync()
        {
            var source = new AwaitableCompletionSource<IapRestoreResult>();
            source.SetResult(IapRestoreResult.Ok(0));
            return source.Awaitable;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Unsubscribe();

            // 대기 중인 구매가 있으면 영원히 매달리지 않게 끊어준다.
            CompletePending(IapPurchaseResult.Failed(new IapError(DisposedCode, "서비스가 해제됐다")));

            _provider.Dispose();
        }

        private async Awaitable<bool> RunInitializeAsync()
        {
            // Unity IAP는 Connect 전에 구독을 끝낼 것을 요구하고, 미확정 구매가 연결 직후에
            // 재전달되기도 한다. 그래서 provider 초기화보다 구독이 먼저다.
            Subscribe();

            var success = await _provider.InitializeAsync(
                new IapProviderContext(BuildDefinitions(), _verboseLogging));

            IsInitialized = success;
            _initializing = false;

            if (!success)
            {
                Debug.LogError($"[IAPService] {_provider.Name} provider 초기화에 실패했다.");
                Unsubscribe();
            }
            else if (_verboseLogging)
            {
                Debug.Log($"[IAPService] {_provider.Name} 초기화 완료. 상품 {_provider.Products.Count}개.");
            }

            CompleteInitWaiters(success);
            return success;
        }

        private List<IapProductDefinition> BuildDefinitions()
        {
            var definitions = new List<IapProductDefinition>(_byId.Count);
            foreach (var pair in _byId) definitions.Add(pair.Value);
            return definitions;
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            _provider.PurchasePending += HandlePending;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            _provider.PurchasePending -= HandlePending;
            _subscribed = false;
        }

        // provider 이벤트 핸들러라 반환값을 기다릴 주체가 없다 — 예외는 전부 안에서 잡는다.
        private async void HandlePending(IapPendingPurchase pending)
        {
            if (_disposed) return;

            if (!_byStoreId.TryGetValue(pending.StoreId ?? string.Empty, out var definition))
            {
                // 확정해 버리면 이 구매는 영영 지급할 수 없다. 다음 버전의 카탈로그가
                // 이 상품을 알게 될 수도 있으므로 미확정으로 남긴다.
                Debug.LogWarning($"[IAPService] 카탈로그에 없는 상품의 구매가 도착했다: {pending.StoreId}. " +
                                 "확정하지 않고 남겨둔다.");
                return;
            }

            var purchase = BuildPurchase(definition, pending);

            if (!_validator.Validate(purchase, out var validationError))
            {
                Debug.LogError($"[IAPService] 영수증 검증에 실패했다: {purchase.ProductId} {validationError}");
                CompletePendingFor(definition.Id, IapPurchaseResult.InvalidReceipt(validationError));
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

            if (_disposed) return;

            if (!fulfilled)
            {
                // 확정하지 않는다 — 스토어가 다음 실행에 같은 구매를 다시 내려준다.
                CompletePendingFor(definition.Id, IapPurchaseResult.Failed(
                    new IapError(FulfillmentFailedCode, "지급에 실패해 확정하지 않았다")));
                return;
            }

            _provider.Confirm(pending.TransactionId);

            if (definition.Type == IapProductType.NonConsumable && !_entitlements.IsOwned(definition.Id))
            {
                _entitlements.SetOwned(definition.Id, true);
                OwnedChanged?.Invoke(definition.Id);
            }

            Purchased?.Invoke(purchase);

            CompletePendingFor(definition.Id, pending.IsRestored
                ? IapPurchaseResult.Restored(purchase)
                : IapPurchaseResult.Purchased(purchase));
        }

        private IapPurchase BuildPurchase(IapProductDefinition definition, IapPendingPurchase pending)
        {
            var price = 0.0;
            var currency = string.Empty;

            if (TryGetProduct(definition.Id, out var product))
            {
                price = product.Price;
                currency = product.CurrencyCode;
            }

            return new IapPurchase(definition.Id, definition.Type, pending.TransactionId,
                                   pending.Receipt, price, currency, pending.IsRestored);
        }

        // 대기 중인 구매의 상품과 일치할 때만 완료시킨다. 재전달·복원처럼 아무도 기다리지
        // 않는 구매는 조용히 지나간다.
        private void CompletePendingFor(string productId, IapPurchaseResult result)
        {
            if (_pendingSource == null || _pendingProductId != productId) return;

            CompletePending(result);
        }

        private void CompletePending(IapPurchaseResult result)
        {
            var source = _pendingSource;
            if (source == null) return;

            ClearPending();
            source.SetResult(result);
        }

        private void ClearPending()
        {
            _pendingSource = null;
            _pendingProductId = null;
        }

        private void CompleteInitWaiters(bool success)
        {
            if (_initWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<bool>>(_initWaiters);
            _initWaiters.Clear();

            foreach (var waiter in waiters) waiter.SetResult(success);
        }

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
