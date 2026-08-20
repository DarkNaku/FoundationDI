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

        private AwaitableCompletionSource<AdShowResult> _showCompletion;
        private string _activePlacement;
        private AdReward? _pendingReward;
        private IDisposable _scheduledClose;

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
            if (_isDisposed) return Immediate(AdShowResult.Failed(new AdError(-1, "서비스가 이미 해제됐다")));

            // 순서가 중요하다. 광고제거는 로드조차 트리거하지 않아야 하므로 가장 먼저 본다.
            if (_blockWhenAdsRemoved && _adsRemoved()) return Immediate(AdShowResult.Blocked());

            if (_showCompletion != null) return Immediate(AdShowResult.Failed(new AdError(-2, "이미 표시 중이다")));

            if (!_adapter.IsReady)
            {
                Load();   // 다음 기회를 위해 미리 채워둔다
                return Immediate(AdShowResult.NotReady());
            }

            _activePlacement = placement;
            _showCompletion = new AwaitableCompletionSource<AdShowResult>();
            _pendingReward = null;

            var awaitable = _showCompletion.Awaitable;
            _adapter.Show();
            return awaitable;
        }

        // Awaitable은 단일 사용이므로 호출자마다 새 completion source를 만든다.
        private static Awaitable<AdShowResult> Immediate(AdShowResult result)
        {
            var source = new AwaitableCompletionSource<AdShowResult>();
            source.SetResult(result);
            return source.Awaitable;
        }

        // 완료는 반드시 이 한 곳을 거친다. 이중 완료를 막고 상태를 함께 청소한다.
        // 실제로 완료시켰는지 돌려준다. 중복 Closed에서 Closed 이벤트가 두 번 나가는 것을 막는다.
        private bool Complete(AdShowResult result)
        {
            var completion = _showCompletion;
            if (completion == null) return false;

            _showCompletion = null;
            _activePlacement = null;
            completion.SetResult(result);
            return true;
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
        private void OnDisplayFailed(AdError error)
        {
            Debug.LogWarning($"[AdService] {_format} 표시 실패: {error}");
            Complete(AdShowResult.Failed(error));
        }
        // 보상은 래치만 한다. 여기서 완료시키면, 보상 후 닫힘 사이에 유저가 앱을 떠나는
        // 경우와 닫힘이 먼저 오는 경우를 구분할 수 없게 된다.
        private void OnRewarded(AdReward reward)
        {
            _pendingReward = reward;
        }

        private void OnClosed()
        {
            // 닫힘이 보상보다 먼저 오는 SDK/네트워크가 있다. 유예 프레임을 두고 기다린다.
            _scheduledClose?.Dispose();

            // 유예 0이면 NextFrames가 콜백을 동기 실행하고 돌아온다. 그때 핸들을 다시
            // 넣으면 이미 발화한 스케줄이 남아 "대기 중" 신호가 거짓이 된다.
            var fired = false;
            var handle = _dispatcher.NextFrames(_rewardGraceFrames, () =>
            {
                fired = true;
                _scheduledClose = null;
                FinalizeClose();
            });

            if (!fired) _scheduledClose = handle;
        }

        private void FinalizeClose()
        {
            if (_isDisposed) return;

            var reward = _pendingReward;
            _pendingReward = null;

            AdShowResult result;
            if (reward.HasValue) result = AdShowResult.Rewarded(reward.Value);
            else if (_format == AdFormat.Rewarded) result = AdShowResult.Dismissed();
            else result = AdShowResult.Shown();

            if (!Complete(result)) return;
            Closed?.Invoke();
        }

        // 어댑터는 배치명을 모른다. 표시 중인 광고의 배치명을 여기서 채워 넣는다.
        private void OnPaid(AdImpression impression)
        {
            Paid?.Invoke(string.IsNullOrEmpty(_activePlacement)
                ? impression
                : impression.WithPlacement(_activePlacement));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            CancelScheduledRetry();
            _scheduledClose?.Dispose();
            _scheduledClose = null;

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
