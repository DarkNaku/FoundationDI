using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 분석 provider SDK 초기화만 맡는다. AnalyticsService에서 떼어 낸 이유는 ConsentService가
    // UMP를 시작하기 전에 이것을 기다려야 하기 때문이다 — Google은 "UMP 전에 Firebase를
    // 초기화하라"고 요구한다. 동의를 기다리지 않으므로 ConsentService가 기다려도 교착이 없다.
    public sealed class AnalyticsProviderInitializer : IConsentPrerequisite
    {
        private readonly List<IAnalyticsProvider> _providers;
        private readonly List<IAnalyticsProvider> _active = new();
        private readonly List<AwaitableCompletionSource<bool>> _waiters = new();

        private bool _running;
        private bool _succeeded;

        public AnalyticsProviderInitializer(IReadOnlyList<IAnalyticsProvider> providers)
        {
            _providers = providers == null
                ? new List<IAnalyticsProvider>()
                : new List<IAnalyticsProvider>(providers);
        }

        // 생성 시점에 받은 전부. 실패한 provider도 남는다 — 재시도와 Dispose의 대상이다.
        public IReadOnlyList<IAnalyticsProvider> Providers => _providers;

        // 초기화에 성공해 팬아웃을 받는 provider들.
        public IReadOnlyList<IAnalyticsProvider> Active => _active;

        // 재진입 안전하다. 진행 중이면 편승하고, 성공한 뒤에는 즉시 true다.
        // provider 중 하나라도 살아나면 성공이다. 전부 실패하면 다음 호출에서 다시 시도한다.
        public Awaitable<bool> InitializeAsync()
        {
            if (_succeeded) return Completed(true);

            var waiter = new AwaitableCompletionSource<bool>();
            _waiters.Add(waiter);

            if (!_running)
            {
                _running = true;
                Run();
            }

            return waiter.Awaitable;
        }

        public async Awaitable PrepareAsync()
        {
            await InitializeAsync();
        }

        private async void Run()
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

            _succeeded = _active.Count > 0;
            _running = false;

            var waiters = new List<AwaitableCompletionSource<bool>>(_waiters);
            _waiters.Clear();

            foreach (var waiter in waiters) waiter.SetResult(_succeeded);
        }

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
