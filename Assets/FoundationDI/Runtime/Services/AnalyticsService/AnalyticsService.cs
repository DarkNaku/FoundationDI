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
            return true;
        }

        public void LogEvent(string name) => LogEvent(name, null);

        public void LogEvent(string name, AnalyticsParams parameters) =>
            Fanout(p => p.LogEvent(name, parameters));

        public void LogPurchase(PurchaseInfo purchase)
        {
            var copy = purchase;
            Fanout(p => p.LogPurchase(copy));
        }

        public void LogAdImpression(AdImpression impression)
        {
            var copy = impression;
            Fanout(p => p.LogAdImpression(copy));
        }

        public void SetUserId(string userId) => Fanout(p => p.SetUserId(userId));

        public void SetUserProperty(string name, string value) =>
            Fanout(p => p.SetUserProperty(name, value));

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
