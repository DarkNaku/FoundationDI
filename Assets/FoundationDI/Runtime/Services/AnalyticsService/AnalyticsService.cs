using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 등록된 모든 provider에 같은 호출을 브로드캐스트한다. 라우팅 규칙은 없다 —
    // 무엇을 무시할지는 각 어댑터(또는 MMP 대시보드)가 결정한다.
    public sealed class AnalyticsService : IAnalyticsService
    {
        // 생성 시점에 받은 전부. 초기화에 실패한 provider도 여기 남는다 — 재시도의 대상이고,
        // Dispose 대상이기도 하기 때문이다.
        private readonly List<IAnalyticsProvider> _providers;

        // 초기화에 성공해 실제로 팬아웃을 받는 provider들.
        private readonly List<IAnalyticsProvider> _active = new();

        // 초기화 전 이벤트. 순서가 의미를 가지므로 큐다. 상한을 두지 않는 이유:
        // 초기화는 보통 수 초 안에 끝나고 그 사이 이벤트는 기껏해야 수십 개다. 상한을 두면
        // "무엇을 버릴 것인가"라는 답 없는 질문이 따라오는데, 그 질문이 값어치하는 만큼의
        // 메모리 위험이 실재하지 않는다.
        private readonly Queue<Action<IAnalyticsProvider>> _pendingEvents = new();

        // 유저 ID와 프로퍼티는 이벤트가 아니라 상태다. 같은 큐에 넣으면 초기화 전에 같은
        // 프로퍼티를 다섯 번 세팅했을 때 다섯 번 전달되는 낭비가 생긴다. latest-wins 슬롯에 둔다.
        private readonly Dictionary<string, string> _pendingProperties = new();
        private string _pendingUserId;
        private bool _hasPendingUserId;

        // 진행 중인 초기화에 편승한 호출자들. Awaitable은 단일 사용이라 하나를 여럿이 await 할 수
        // 없으므로, 호출자마다 별도의 완료 소스를 만들어 두었다가 한꺼번에 같은 결과로 완료시킨다.
        private readonly List<AwaitableCompletionSource<bool>> _initWaiters = new();
        private bool _initializing;

        private bool _collectionEnabled;
        private bool _disposed;

        public AnalyticsService(IReadOnlyList<IAnalyticsProvider> providers, AnalyticsServiceOptions options)
        {
            _providers = providers == null
                ? new List<IAnalyticsProvider>()
                : new List<IAnalyticsProvider>(providers);

            _collectionEnabled = options.CollectionEnabledByDefault;
        }

        public bool IsInitialized { get; private set; }

        // 수집이 꺼져 있으면 버퍼에도 넣지 않는다. 동의 전에 쌓아 뒀다가 동의 시점에
        // 소급 전송하는 것은 게이트를 두는 의미 자체를 없앤다.
        private bool CanCollect => !_disposed && _collectionEnabled;

        // false면 모든 로깅이 호출 즉시 드롭되고, provider에도 SDK 레벨로 끄라고 알린다.
        // 후자가 없으면 게이트가 사실상 무력하다 — Firebase 같은 SDK는 우리가 LogEvent를
        // 부르지 않아도 세션·화면 이벤트를 자동 수집하기 때문이다.
        public bool CollectionEnabled
        {
            get => _collectionEnabled;
            set
            {
                if (_disposed) return;
                if (_collectionEnabled == value) return;

                _collectionEnabled = value;
                Fanout(p => p.SetCollectionEnabled(value));
            }
        }

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

        public void LogEvent(string name) => LogEvent(name, null);

        public void LogEvent(string name, AnalyticsParams parameters) =>
            Dispatch(p => p.LogEvent(name, parameters));

        public void LogPurchase(PurchaseInfo purchase)
        {
            var copy = purchase;
            Dispatch(p => p.LogPurchase(copy));
        }

        public void LogAdImpression(AdImpression impression)
        {
            var copy = impression;
            Dispatch(p => p.LogAdImpression(copy));
        }

        public void SetUserId(string userId)
        {
            if (!CanCollect) return;

            if (!IsInitialized)
            {
                _pendingUserId = userId;
                _hasPendingUserId = true;
                return;
            }

            Fanout(p => p.SetUserId(userId));
        }

        public void SetUserProperty(string name, string value)
        {
            if (!CanCollect) return;

            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("[AnalyticsService] 이름이 비어 있는 유저 프로퍼티는 무시한다.");
                return;
            }

            if (!IsInitialized)
            {
                _pendingProperties[name] = value;
                return;
            }

            Fanout(p => p.SetUserProperty(name, value));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            foreach (var provider in _providers)
            {
                try
                {
                    provider.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AnalyticsService] {provider.Name} 해제 중 예외: {e}");
                }
            }

            _providers.Clear();
            _active.Clear();
            ClearPending();
        }

        // provider 중 하나라도 살아나면 서비스는 초기화된 것으로 본다. Firebase 하나가 죽었다고
        // 전체 분석이 멎을 이유가 없다. 전부 실패하면 초기화되지 않은 상태로 남고 버퍼도 유지되므로,
        // 다시 호출하면 그대로 재시도된다 — 네트워크 없이 앱을 켠 경우가 실제로 이 경로다.
        private async Awaitable<bool> RunInitializeAsync()
        {
            _active.Clear();

            foreach (var provider in _providers)
            {
                var ok = false;

                try
                {
                    ok = await provider.InitializeAsync();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AnalyticsService] {provider.Name} 초기화 중 예외: {e}");
                }

                if (ok)
                {
                    _active.Add(provider);
                }
                else
                {
                    Debug.LogError($"[AnalyticsService] {provider.Name} 초기화에 실패했다. 팬아웃에서 제외한다.");
                }
            }

            var succeeded = _active.Count > 0;

            if (succeeded)
            {
                IsInitialized = true;
                Flush();
            }

            _initializing = false;
            CompleteWaiters(succeeded);
            return succeeded;
        }

        private void CompleteWaiters(bool result)
        {
            if (_initWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<bool>>(_initWaiters);
            _initWaiters.Clear();

            foreach (var waiter in waiters) waiter.SetResult(result);
        }

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }

        // 초기화 전이면 큐에 담고, 초기화 후면 즉시 팬아웃한다.
        private void Dispatch(Action<IAnalyticsProvider> action)
        {
            if (!CanCollect) return;

            if (!IsInitialized)
            {
                _pendingEvents.Enqueue(action);
                return;
            }

            Fanout(action);
        }

        // 유저 상태를 먼저, 버퍼된 이벤트를 나중에 내보낸다. 유저 귀속이 붙은 상태로
        // 이벤트가 나가야 하기 때문이다 — 순서가 뒤집히면 첫 이벤트들이 익명으로 집계된다.
        private void Flush()
        {
            var enabled = _collectionEnabled;
            Fanout(p => p.SetCollectionEnabled(enabled));

            if (_hasPendingUserId)
            {
                var userId = _pendingUserId;
                Fanout(p => p.SetUserId(userId));
                _hasPendingUserId = false;
                _pendingUserId = null;
            }

            foreach (var pair in _pendingProperties)
            {
                var name = pair.Key;
                var value = pair.Value;
                Fanout(p => p.SetUserProperty(name, value));
            }

            _pendingProperties.Clear();

            while (_pendingEvents.Count > 0)
            {
                Fanout(_pendingEvents.Dequeue());
            }

            // 반드시 마지막이다. Adjust 어댑터가 여기서 첫 세션 지연을 푸는데(IAnalyticsFlushHook),
            // 이 줄이 위로 올라가면 아직 전달되지 않은 파라미터가 첫 세션에서 빠진다.
            Fanout(p => (p as IAnalyticsFlushHook)?.OnBufferedStateFlushed());
        }

        private void ClearPending()
        {
            _pendingEvents.Clear();
            _pendingProperties.Clear();
            _pendingUserId = null;
            _hasPendingUserId = false;
        }

        // provider 하나가 던진 예외가 나머지 provider의 전송을 막지 않게 한다.
        // 분석은 게임 로직이 아니라 곁다리이므로, SDK 하나가 죽었다고 나머지까지 멎으면 안 된다.
        private void Fanout(Action<IAnalyticsProvider> action)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                var provider = _active[i];

                try
                {
                    action(provider);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AnalyticsService] {provider.Name} 에서 예외: {e}");
                }
            }
        }
    }
}
