using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 스토어 없이 구매 플로우 전체를 에디터에서 돌리기 위한 가짜 스토어.
    //
    // 실제 스토어와 맞춘 두 가지 규율이 있다.
    //  1) 확정(Confirm)되기 전에는 소유로 기록하지 않는다.
    //  2) 이미 확정된 구매는 다음 실행에 재전달하지 않는다 — 복원(RestoreAsync)으로만 되돌아온다.
    public sealed class DummyIapProvider : IIapProvider
    {
        private const string OwnedKeyPrefix = "FoundationDI.IAP.Dummy.Owned.";

        private readonly DummyIapOptions _options;
        private readonly List<IapProduct> _products = new();
        private readonly Dictionary<string, IapProductDefinition> _byStoreId = new();

        // 아직 확정되지 않은 거래. 확정 시 어떤 상품이었는지 알아야 소유를 기록할 수 있다.
        private readonly Dictionary<string, IapProductDefinition> _unconfirmed = new();

        private int _sequence;
        private bool _disposed;

        public DummyIapProvider(DummyIapOptions options)
        {
            _options = options;
        }

        public string Name => "Dummy";

        public IReadOnlyList<IapProduct> Products => _products;

        public event Action<IapPendingPurchase> PurchasePending;
        public event Action<IapPurchaseFailure> PurchaseFailed;

        // Dummy는 보류(iOS Ask-to-Buy)를 흉내내지 않는다 — 승인 주체가 없으니 재현할 대상이
        // 없기 때문이다. IIapProvider가 요구하는 멤버라 선언은 남기고 경고만 끈다.
        // (Dispose에서 null을 대입하므로 CS0067이 아니라 CS0414가 뜬다.)
#pragma warning disable CS0414
        public event Action<string> PurchaseDeferred;
#pragma warning restore CS0414

        public Awaitable<bool> InitializeAsync(IapProviderContext context)
        {
            _products.Clear();
            _byStoreId.Clear();

            if (context.Products != null)
            {
                foreach (var definition in context.Products)
                {
                    if (string.IsNullOrEmpty(definition.StoreId)) continue;

                    _byStoreId[definition.StoreId] = definition;
                    _products.Add(new IapProduct(definition.Id, definition.StoreId, definition.Type,
                        $"{definition.Id} (Dummy)", "Dummy provider가 만든 가짜 상품",
                        _options.PriceFormat, 0.99, "USD", true));
                }
            }

            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public bool Purchase(string storeId)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(storeId) || !_byStoreId.TryGetValue(storeId, out var definition)) return false;

            RunPurchase(definition);
            return true;
        }

        public void Confirm(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId)) return;
            if (!_unconfirmed.Remove(transactionId, out var definition)) return;

            if (definition.Type == IapProductType.NonConsumable)
            {
                PlayerPrefs.SetInt(OwnedKeyPrefix + definition.StoreId, 1);
                PlayerPrefs.Save();
            }
        }

        public Awaitable<bool> RestoreAsync()
        {
            foreach (var pair in _byStoreId)
            {
                var definition = pair.Value;

                if (definition.Type != IapProductType.NonConsumable) continue;
                if (PlayerPrefs.GetInt(OwnedKeyPrefix + definition.StoreId, 0) == 0) continue;

                Emit(definition, isRestored: true);
            }

            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public void Dispose()
        {
            _disposed = true;
            _unconfirmed.Clear();
            PurchasePending = null;
            PurchaseFailed = null;
            PurchaseDeferred = null;
        }

        // 지연이 0이면 동기적으로 끝난다 — 테스트가 프레임을 기다릴 필요가 없다.
        private async void RunPurchase(IapProductDefinition definition)
        {
            if (_options.DelaySeconds > 0f)
            {
                try
                {
                    await Awaitable.WaitForSecondsAsync(_options.DelaySeconds);
                }
                catch (OperationCanceledException)
                {
                    return;   // 플레이 모드 종료
                }

                if (_disposed) return;
            }

            if (_options.AlwaysCancel)
            {
                PurchaseFailed?.Invoke(new IapPurchaseFailure(definition.StoreId, true,
                    new IapError(0, "사용자가 취소했다 (Dummy)")));
                return;
            }

            if (_options.AlwaysFail)
            {
                PurchaseFailed?.Invoke(new IapPurchaseFailure(definition.StoreId, false,
                    new IapError(-1, "구매에 실패했다 (Dummy)")));
                return;
            }

            Emit(definition, isRestored: false);
        }

        private void Emit(IapProductDefinition definition, bool isRestored)
        {
            var transactionId = $"dummy-{definition.StoreId}-{_sequence++}";
            _unconfirmed[transactionId] = definition;

            PurchasePending?.Invoke(new IapPendingPurchase(definition.StoreId, transactionId,
                $"{{\"dummy\":\"{transactionId}\"}}", isRestored));
        }
    }
}
