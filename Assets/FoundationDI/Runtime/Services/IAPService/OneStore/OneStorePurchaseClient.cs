// 원스토어 플러그인과 같은 조건이다 — 에디터에서는 빌드 타깃이 안드로이드일 때만 SDK 타입이 있다.
#if UNITY_ANDROID || !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OneStore.Auth;
using OneStore.Purchasing;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 원스토어 Unity 플러그인(PurchaseClientImpl)을 IOneStoreClient로 바꾼다. 기기에서만 검증된다(단위 테스트 없음).
    //
    // 플러그인의 콜백은 요청과 짝지어 오지 않는다 — 구매 조회와 구매 흐름의 결과가 같은 OnPurchaseSucceeded로
    // 오고, 소비·승인 실패에는 어느 구매인지가 없다. 그래서 요청을 하나씩만 보내고 콜백을 그 요청의 결과로 본다.
    // 진행 중인 요청이 없을 때 온 구매는 PurchasesUpdated로 낸다.
    internal sealed class OneStorePurchaseClient : IOneStoreClient, IPurchaseCallback
    {
        // 플러그인이 콜백을 끝내 부르지 않는 경로가 있다 — 연결 끊김(ERROR_SERVICE_DISCONNECTED)은 아무 콜백도
        // 없고, 승인 뒤 재조회에 그 구매가 없으면 OnAcknowledgeSucceeded가 오지 않는다. 요청을 하나씩 보내므로
        // 하나가 멈추면 뒤가 전부 멈춘다. 구매 흐름(결제 화면) 밖의 요청은 이 시간 뒤 실패로 끝내고 다음으로 간다.
        private const float RequestTimeoutSeconds = 60f;

        private static readonly IReadOnlyList<OneStoreProduct> NoProducts = Array.Empty<OneStoreProduct>();
        private static readonly IReadOnlyList<OneStorePurchase> NoPurchases = Array.Empty<OneStorePurchase>();

        private readonly string _licenseKey;
        private readonly Func<IMainThreadDispatcher> _dispatcherFactory;
        private IMainThreadDispatcher _dispatcher;

        private PurchaseClientImpl _client;
        private readonly Queue<Request> _queue = new();
        private Request _active;
        private bool _disposed;

        public OneStorePurchaseClient(string licenseKey, Func<IMainThreadDispatcher> dispatcherFactory)
        {
            _licenseKey = licenseKey;
            _dispatcherFactory = dispatcherFactory;
        }

        public event Action<IReadOnlyList<OneStorePurchase>> PurchasesUpdated;

        public void QueryProducts(IReadOnlyList<string> productIds,
                                  Action<OneStoreResult, IReadOnlyList<OneStoreProduct>> onDone)
        {
            var ids = new ReadOnlyCollection<string>(new List<string>(productIds ?? Array.Empty<string>()));

            Enqueue(new Request(RequestKind.Products, client => client.QueryProductDetails(ids, ProductType.INAPP))
            {
                OnProducts = onDone,
            });
        }

        public void QueryPurchases(Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone)
        {
            Enqueue(new Request(RequestKind.Purchases, client => client.QueryPurchases(ProductType.INAPP))
            {
                OnPurchases = onDone,
            });
        }

        public void Purchase(string productId, Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> onDone)
        {
            var flowParams = new PurchaseFlowParams.Builder()
                .SetProductId(productId)
                .SetProductType(ProductType.INAPP)
                .SetQuantity(1)
                .Build();

            Enqueue(new Request(RequestKind.Purchase, client => client.Purchase(flowParams))
            {
                OnPurchases = onDone,
            });
        }

        public void Consume(OneStorePurchase purchase, Action<OneStoreResult> onDone)
        {
            if (purchase?.Source is not PurchaseData data)
            {
                onDone?.Invoke(new OneStoreResult(OneStoreResponseCodes.NoResponse, "원스토어 구매 객체가 없다"));
                return;
            }

            Enqueue(new Request(RequestKind.Consume, client => client.ConsumePurchase(data)) { OnConfirmed = onDone });
        }

        public void Acknowledge(OneStorePurchase purchase, Action<OneStoreResult> onDone)
        {
            if (purchase?.Source is not PurchaseData data)
            {
                onDone?.Invoke(new OneStoreResult(OneStoreResponseCodes.NoResponse, "원스토어 구매 객체가 없다"));
                return;
            }

            Enqueue(new Request(RequestKind.Acknowledge, client => client.AcknowledgePurchase(data, ProductType.INAPP))
            {
                OnConfirmed = onDone,
            });
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            FailAll(new OneStoreResult(OneStoreResponseCodes.Disposed, "클라이언트가 해제됐다"));
            ResetClient();
        }

        // ── IPurchaseCallback (플러그인이 메인 스레드에서 부른다) ─────────────────

        public void OnSetupFailed(IapResult iapResult)
        {
            // 플러그인은 연결 실패 때 자기 대기열을 비우고 연결 상태를 '연결 중'에 남긴다 — 이 인스턴스로는 다시
            // 연결할 수 없다. 다음 요청이 새로 만들게 버린다.
            ResetClient();
            FailAll(ToResult(iapResult));
        }

        public void OnProductDetailsSucceeded(List<ProductDetail> productDetails)
        {
            var products = new List<OneStoreProduct>();

            foreach (var detail in productDetails ?? new List<ProductDetail>())
            {
                if (detail == null) continue;

                products.Add(new OneStoreProduct(detail.productId, detail.title, detail.price,
                                                 detail.priceCurrencyCode, detail.priceAmountMicros));
            }

            Finish(RequestKind.Products, request => request.OnProducts?.Invoke(OneStoreResult.Ok, products));
        }

        public void OnProductDetailsFailed(IapResult iapResult) => FinishFailed(RequestKind.Products, iapResult);

        public void OnPurchaseSucceeded(List<PurchaseData> purchases)
        {
            var mapped = ToPurchases(purchases);

            if (IsActive(RequestKind.Purchases) || IsActive(RequestKind.Purchase))
            {
                Finish(_active.Kind, request => request.OnPurchases?.Invoke(OneStoreResult.Ok, mapped));
                return;
            }

            PurchasesUpdated?.Invoke(mapped);
        }

        public void OnPurchaseFailed(IapResult iapResult)
        {
            if (IsActive(RequestKind.Purchases) || IsActive(RequestKind.Purchase))
            {
                FinishFailed(_active.Kind, iapResult);
                return;
            }

            Debug.LogWarning($"[OneStoreIAPService] 요청 없이 구매 실패가 왔다: {ToResult(iapResult)}");
        }

        public void OnConsumeSucceeded(PurchaseData purchase) =>
            Finish(RequestKind.Consume, request => request.OnConfirmed?.Invoke(OneStoreResult.Ok));

        public void OnConsumeFailed(IapResult iapResult) => FinishFailed(RequestKind.Consume, iapResult);

        public void OnAcknowledgeSucceeded(PurchaseData purchase, ProductType type) =>
            Finish(RequestKind.Acknowledge, request => request.OnConfirmed?.Invoke(OneStoreResult.Ok));

        public void OnAcknowledgeFailed(IapResult iapResult) => FinishFailed(RequestKind.Acknowledge, iapResult);

        // 구독은 다루지 않는다.
        public void OnManageRecurringProduct(IapResult iapResult, PurchaseData purchase, RecurringAction action)
        {
        }

        // 원스토어 서비스(OSS) 앱이 없거나 낡았다. 설치·업데이트 화면을 띄우고, 끝나면 로그인한 뒤 다시 보낸다.
        public void OnNeedUpdate()
        {
            var request = BeginRecovery(OneStoreResponseCodes.NeedUpdate);
            if (request == null) return;

            if (_client == null)
            {
                FinishRecovery(request, new OneStoreResult(OneStoreResponseCodes.NeedUpdate, "원스토어 서비스 업데이트가 필요하다"));
                return;
            }

            _client.LaunchUpdateOrInstallFlow(result =>
            {
                if (!result.IsSuccessful())
                {
                    FinishRecovery(request, ToResult(result));
                    return;
                }

                SignIn(request);
            });
        }

        // 원스토어에 로그인하지 않았다. 로그인 화면을 띄우고, 성공하면 같은 요청을 다시 보낸다.
        public void OnNeedLogin()
        {
            var request = BeginRecovery(OneStoreResponseCodes.NeedLogin);
            if (request != null) SignIn(request);
        }

        // ── 요청 대기열 ─────────────────────────────────────────

        private void Enqueue(Request request)
        {
            if (_disposed)
            {
                request.Fail(new OneStoreResult(OneStoreResponseCodes.Disposed, "클라이언트가 해제됐다"));
                return;
            }

            _queue.Enqueue(request);
            Pump();
        }

        private void Pump()
        {
            if (_active != null || _queue.Count == 0 || _disposed) return;

            _active = _queue.Dequeue();
            Issue(_active);
        }

        private void Issue(Request request)
        {
            // 결제 화면은 사용자가 결제 수단을 고르는 시간까지 들어가 시간 제한을 두지 않는다.
            if (request.Kind != RequestKind.Purchase) request.Timeout = StartTimeout(request);

            try
            {
                EnsureClient();
                request.Issue(_client);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OneStoreIAPService] 원스토어 요청을 보내지 못했다: {request.Kind} {e}");
                ResetClient();
                Complete(request, r => r.Fail(new OneStoreResult(OneStoreResponseCodes.NoResponse, e.Message)));
            }
        }

        private void EnsureClient()
        {
            if (_client != null) return;

            _client = new PurchaseClientImpl(_licenseKey);
            _client.Initialize(this);
        }

        private void ResetClient()
        {
            var client = _client;
            _client = null;

            try
            {
                client?.EndConnection();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OneStoreIAPService] 원스토어 연결을 닫다 예외가 났다: {e.Message}");
            }
        }

        private IDisposable StartTimeout(Request request)
        {
            _dispatcher ??= _dispatcherFactory?.Invoke();

            return _dispatcher?.Delay(RequestTimeoutSeconds, () =>
            {
                if (_active != request) return;

                Debug.LogWarning($"[OneStoreIAPService] 원스토어가 {RequestTimeoutSeconds}초 안에 응답하지 않았다: {request.Kind}");

                // 플러그인 내부 상태(요청 중 표시)가 남아 같은 요청을 무시할 수 있다 — 새 인스턴스로 다시 시작한다.
                ResetClient();
                Complete(request, r => r.Fail(new OneStoreResult(OneStoreResponseCodes.NoResponse, "원스토어가 응답하지 않았다")));
            });
        }

        private bool IsActive(RequestKind kind) => _active != null && _active.Kind == kind && !_active.Recovering;

        private void Finish(RequestKind kind, Action<Request> complete)
        {
            if (!IsActive(kind)) return;

            Complete(_active, complete);
        }

        private void FinishFailed(RequestKind kind, IapResult iapResult)
        {
            var result = ToResult(iapResult);
            Finish(kind, request => request.Fail(result));
        }

        private void Complete(Request request, Action<Request> complete)
        {
            if (_active != request) return;

            request.Timeout?.Dispose();
            _active = null;

            try
            {
                complete(request);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OneStoreIAPService] 원스토어 응답을 처리하다 예외가 났다: {request.Kind} {e}");
            }

            Pump();
        }

        private void FailAll(OneStoreResult result)
        {
            var requests = new List<Request>();
            if (_active != null) requests.Add(_active);
            requests.AddRange(_queue);

            foreach (var request in requests) request.Timeout?.Dispose();

            _active = null;
            _queue.Clear();

            foreach (var request in requests)
            {
                try
                {
                    request.Fail(result);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[OneStoreIAPService] 원스토어 실패를 처리하다 예외가 났다: {request.Kind} {e}");
                }
            }
        }

        // ── 로그인·업데이트 복구 ──────────────────────────────────

        // 한 요청은 한 번만 복구한다. 복구 중에는 시간 제한을 멈춘다(로그인 화면에 머무는 시간).
        private Request BeginRecovery(int code)
        {
            var request = _active;
            if (request == null) return null;

            if (request.Recovered)
            {
                Complete(request, r => r.Fail(new OneStoreResult(code, "원스토어 로그인·업데이트 뒤에도 요청이 거절됐다")));
                return null;
            }

            request.Recovered = true;
            request.Recovering = true;
            request.Timeout?.Dispose();
            request.Timeout = null;
            return request;
        }

        private void SignIn(Request request)
        {
            try
            {
                new OneStoreAuthClientImpl().LaunchSignInFlow(result =>
                {
                    if (!result.IsSuccessful())
                    {
                        FinishRecovery(request, new OneStoreResult(result.Code, result.Message));
                        return;
                    }

                    if (_disposed || _active != request) return;

                    // 연결 실패를 겪은 인스턴스는 다시 연결하지 못하므로 새로 만들어 같은 요청을 보낸다.
                    request.Recovering = false;
                    ResetClient();
                    Issue(request);
                });
            }
            catch (Exception e)
            {
                FinishRecovery(request, new OneStoreResult(OneStoreResponseCodes.NeedLogin, e.Message));
            }
        }

        private void FinishRecovery(Request request, OneStoreResult result)
        {
            if (_disposed || _active != request) return;

            Debug.LogWarning($"[OneStoreIAPService] 원스토어 로그인·업데이트에 실패했다: {result}");
            Complete(request, r => r.Fail(result));
        }

        // ── 변환 ──────────────────────────────────────────────

        private static OneStoreResult ToResult(IapResult iapResult) =>
            iapResult == null
                ? new OneStoreResult(OneStoreResponseCodes.NoResponse, "원스토어 결과가 비어 있다")
                : new OneStoreResult(iapResult.Code, iapResult.Message);

        private static IReadOnlyList<OneStorePurchase> ToPurchases(List<PurchaseData> purchases)
        {
            if (purchases == null || purchases.Count == 0) return NoPurchases;

            var mapped = new List<OneStorePurchase>(purchases.Count);

            foreach (var data in purchases)
            {
                if (data == null) continue;

                mapped.Add(new OneStorePurchase(data.ProductId, data.OrderId, data.PurchaseToken, data.PurchaseState,
                                                data.Acknowledged, data.JsonReceipt, data));
            }

            return mapped;
        }

        private enum RequestKind { Products, Purchases, Purchase, Consume, Acknowledge }

        private sealed class Request
        {
            public readonly RequestKind Kind;
            public readonly Action<PurchaseClientImpl> Issue;

            public Action<OneStoreResult, IReadOnlyList<OneStoreProduct>> OnProducts;
            public Action<OneStoreResult, IReadOnlyList<OneStorePurchase>> OnPurchases;
            public Action<OneStoreResult> OnConfirmed;

            public IDisposable Timeout;
            public bool Recovered;
            public bool Recovering;

            public Request(RequestKind kind, Action<PurchaseClientImpl> issue)
            {
                Kind = kind;
                Issue = issue;
            }

            public void Fail(OneStoreResult result)
            {
                OnProducts?.Invoke(result, NoProducts);
                OnPurchases?.Invoke(result, NoPurchases);
                OnConfirmed?.Invoke(result);
            }
        }
    }
}

#endif
