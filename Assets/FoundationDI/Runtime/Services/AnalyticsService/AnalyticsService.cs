using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 등록된 모든 provider에 같은 호출을 브로드캐스트한다. 라우팅 규칙은 없다 —
    // 무엇을 무시할지는 각 어댑터(또는 MMP 대시보드)가 결정한다.
    public sealed class AnalyticsService : IAnalyticsService
    {
        private readonly List<IAnalyticsProvider> _providers;

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

        private bool _collectionEnabled;

        public AnalyticsService(IReadOnlyList<IAnalyticsProvider> providers, AnalyticsServiceOptions options)
        {
            _providers = providers == null
                ? new List<IAnalyticsProvider>()
                : new List<IAnalyticsProvider>(providers);

            _collectionEnabled = options.CollectionEnabledByDefault;
        }

        public bool IsInitialized { get; private set; }

        public bool CollectionEnabled
        {
            get => _collectionEnabled;
            set => _collectionEnabled = value;
        }

        public async Awaitable<bool> InitializeAsync()
        {
            foreach (var provider in _providers)
            {
                await provider.InitializeAsync();
            }

            IsInitialized = true;
            Flush();
            return true;
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
            ClearPending();
        }

        // 초기화 전이면 큐에 담고, 초기화 후면 즉시 팬아웃한다.
        private void Dispatch(Action<IAnalyticsProvider> action)
        {
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
            for (var i = 0; i < _providers.Count; i++)
            {
                var provider = _providers[i];

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
