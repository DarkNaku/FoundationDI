using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고의 정책 계층. provider를 전혀 모르고 어댑터 seam만 안다.
    // 재시도, 자동 재로드, 보상 확정, 광고제거 게이트가 전부 여기 있다 —
    // 어댑터마다 복붙되지 않도록 하는 것이 이 클래스의 존재 이유다.
    public class FullScreenAdUnit : IInterstitialAd, IRewardedAd, IDisposable
    {
        private readonly IFullScreenAdapter _adapter;
        private readonly IAdDispatcher _dispatcher;
        private readonly AdFormat _format;
        private readonly AdRetryPolicy _retryPolicy;
        private readonly int _rewardGraceFrames;
        private readonly Func<bool> _adsRemoved;

        // 전면은 광고제거 시 차단, 보상은 항상 허용. format에서 유도해 호출자가 틀릴 여지를 없앤다.
        private readonly bool _blockWhenAdsRemoved;

        private int _retryAttempt;
        private IDisposable _scheduledRetry;
        private bool _isDisposed;

        public event Action Loaded;
        public event Action Displayed;
        public event Action Closed;
        public event Action<AdImpression> Paid;

        public FullScreenAdUnit(IFullScreenAdapter adapter, IAdDispatcher dispatcher, AdFormat format,
                                AdRetryPolicy retryPolicy, int rewardGraceFrames, Func<bool> adsRemoved)
        {
            _adapter = adapter;
            _dispatcher = dispatcher;
            _format = format;
            _retryPolicy = retryPolicy;
            _rewardGraceFrames = Mathf.Max(0, rewardGraceFrames);
            _adsRemoved = adsRemoved ?? (() => false);
            _blockWhenAdsRemoved = format == AdFormat.Interstitial;

            _adapter.Loaded += OnLoaded;
            _adapter.LoadFailed += OnLoadFailed;
            _adapter.Displayed += OnDisplayed;
            _adapter.DisplayFailed += OnDisplayFailed;
            _adapter.Closed += OnClosed;
            _adapter.Rewarded += OnRewarded;
            _adapter.Paid += OnPaid;
        }

        public bool IsReady => !_isDisposed && _adapter.IsReady;

        public void Load()
        {
            if (_isDisposed) return;

            CancelScheduledRetry();
            _adapter.Load();
        }

        public Awaitable<AdShowResult> ShowAsync(string placement = null)
        {
            // Task 4에서 구현한다.
            var source = new AwaitableCompletionSource<AdShowResult>();
            source.SetResult(AdShowResult.NotReady());
            return source.Awaitable;
        }

        private void OnLoaded()
        {
            _retryAttempt = 0;
            Loaded?.Invoke();
        }

        private void OnLoadFailed(AdError error)
        {
            ScheduleRetry(error);
        }

        private void ScheduleRetry(AdError error)
        {
            _retryAttempt++;

            if (_retryAttempt > _retryPolicy.MaxAttempts)
            {
                Debug.LogError($"[AdService] {_format} 로드가 {_retryPolicy.MaxAttempts}회 재시도 후에도 실패했다: {error}");
                return;
            }

            var delay = _retryPolicy.DelayFor(_retryAttempt);
            CancelScheduledRetry();
            _scheduledRetry = _dispatcher.Delay(delay, () =>
            {
                _scheduledRetry = null;
                if (!_isDisposed) _adapter.Load();
            });
        }

        private void CancelScheduledRetry()
        {
            _scheduledRetry?.Dispose();
            _scheduledRetry = null;
        }

        private void OnDisplayed() => Displayed?.Invoke();
        private void OnDisplayFailed(AdError error) { }   // Task 4
        private void OnClosed() { }                        // Task 5
        private void OnRewarded(AdReward reward) { }       // Task 5
        private void OnPaid(AdImpression impression) => Paid?.Invoke(impression);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            CancelScheduledRetry();

            _adapter.Loaded -= OnLoaded;
            _adapter.LoadFailed -= OnLoadFailed;
            _adapter.Displayed -= OnDisplayed;
            _adapter.DisplayFailed -= OnDisplayFailed;
            _adapter.Closed -= OnClosed;
            _adapter.Rewarded -= OnRewarded;
            _adapter.Paid -= OnPaid;

            _adapter.Dispose();
        }
    }
}
