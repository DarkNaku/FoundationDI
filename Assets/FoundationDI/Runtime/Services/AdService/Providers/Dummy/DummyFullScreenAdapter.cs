using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class DummyFullScreenAdapter : IFullScreenAdapter
    {
        private readonly AdFormat _format;
        private readonly string _adUnitId;
        private readonly IAdDispatcher _dispatcher;
        private readonly IDummyAdScreen _screen;
        private readonly DummyAdOptions _options;
        private readonly Func<float> _random;

        private IDisposable _pendingLoad;
        private bool _isLoading;
        private bool _isDisposed;

        public bool IsReady { get; private set; }

        public event Action Loaded;
        public event Action<AdError> LoadFailed;
        public event Action Displayed;
        public event Action<AdError> DisplayFailed;
        public event Action Closed;
        public event Action<AdReward> Rewarded;
        public event Action<AdImpression> Paid;

        public DummyFullScreenAdapter(AdFormat format, string adUnitId, IAdDispatcher dispatcher,
                                      IDummyAdScreen screen, DummyAdOptions options, Func<float> random)
        {
            _format = format;
            _adUnitId = adUnitId;
            _dispatcher = dispatcher;
            _screen = screen;
            _options = options;
            _random = random ?? (() => UnityEngine.Random.value);
        }

        public void Load()
        {
            if (_isDisposed || IsReady || _isLoading) return;

            _isLoading = true;
            _pendingLoad = _dispatcher.Delay(_options.LoadDelaySeconds, () =>
            {
                _pendingLoad = null;
                _isLoading = false;
                if (_isDisposed) return;

                if (_random() < _options.FailureRate)
                {
                    LoadFailed?.Invoke(new AdError(3, "dummy: no fill"));
                    return;
                }

                IsReady = true;
                Loaded?.Invoke();
            });
        }

        public void Show()
        {
            if (_isDisposed) return;

            if (!IsReady)
            {
                DisplayFailed?.Invoke(new AdError(-3, "dummy: 준비되지 않은 광고를 표시하려 했다"));
                return;
            }

            IsReady = false;
            Displayed?.Invoke();
            EmitImpression();

            _screen.ShowFullScreen(_format, _options.AdDurationSeconds,
                onSkip: () => Closed?.Invoke(),
                onComplete: () =>
                {
                    // 보상은 닫힘보다 먼저 보낸다 — AdMob/MAX의 일반적인 순서를 흉내낸다.
                    if (_format == AdFormat.Rewarded) Rewarded?.Invoke(new AdReward("dummy_reward", 1));
                    Closed?.Invoke();
                });
        }

        // 가짜 네트워크명과 난수 단가로 임프레션을 발행한다.
        // 3사 어댑터가 없는 지금도 분석 연동 경로를 실기에서 검증할 수 있게 하는 것이 목적이다.
        // AdUnitId는 provider가 전달받은 실제 값을 그대로 싣는다 — 합성 문자열을 쓰면
        // 배치별로 어떤 유닛이 얼마를 버는지를 실기에서 검증할 수 없다.
        private void EmitImpression()
        {
            var revenue = 0.001 + _random() * 0.05;
            Paid?.Invoke(new AdImpression(
                _format, "Dummy", "DummyNetwork", _adUnitId,
                "dummy-instance", null, revenue, "USD", AdRevenuePrecision.Estimated, "dummy-creative"));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _pendingLoad?.Dispose();
            _pendingLoad = null;
            IsReady = false;
        }
    }
}
