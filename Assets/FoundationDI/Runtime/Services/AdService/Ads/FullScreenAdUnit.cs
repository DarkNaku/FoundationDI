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
        private readonly float _cooldownSeconds;
        private readonly Func<bool> _adsRemoved;

        // 전면은 광고제거 시 차단, 보상은 항상 허용. format에서 유도해 호출자가 틀릴 여지를 없앤다.
        private readonly bool _blockWhenAdsRemoved;

        private int _retryAttempt;
        private IDisposable _scheduledRetry;
        private bool _isDisposed;
        private bool _isLoadInFlight;

        private bool _isCoolingDown;
        private IDisposable _scheduledCooldown;

        private AwaitableCompletionSource<AdShowResult> _showCompletion;
        private string _activePlacement;
        private AdReward? _pendingReward;
        private IDisposable _scheduledClose;

        public event Action Loaded;
        public event Action Displayed;
        public event Action Closed;
        public event Action<AdImpression> Paid;

        public FullScreenAdUnit(IFullScreenAdapter adapter, IAdDispatcher dispatcher, AdFormat format,
                                AdRetryPolicy retryPolicy, int rewardGraceFrames, float cooldownSeconds,
                                Func<bool> adsRemoved)
        {
            _adapter = adapter;
            _dispatcher = dispatcher;
            _format = format;
            _retryPolicy = retryPolicy;
            _rewardGraceFrames = Mathf.Max(0, rewardGraceFrames);
            _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
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

        // 지금 ShowAsync를 부르면 실제로 표시될지: 해제 안 됐고, AdsRemoved에 막히지 않았고,
        // 쿨다운 중이 아니고, 이미 표시 요청이 진행 중이지 않고(요청~표시 사이 포함), 준비됐다.
        // _showCompletion을 넣는 이유: "지금 부르면 실제로 표시가 시작될지"라는 계약을 어기면
        // 안 되기 때문이다 — 재진입 자체는 ShowAsync가 Failed(-2)로 별도 진단하지만, 그건
        // "왜 안 됐는지"의 문제고 CanShow는 "될지 안 될지"의 문제라 여기선 진행 중도 거짓이어야 한다.
        public bool CanShow => !_isDisposed
                               && !(_blockWhenAdsRemoved && _adsRemoved())
                               && !_isCoolingDown
                               && _showCompletion == null
                               && _adapter.IsReady;

        public void Load()
        {
            if (_isDisposed) return;

            // 명시적 Load 호출은 새 시도의 시작이다. 리셋하지 않으면 한 번 예산을
            // 소진한 뒤로는(예: 초기 자동로드가 네트워크 없이 실패) 닫힘 후 자동
            // 재로드나 NotReady의 ShowAsync가 트리거하는 Load()가 매번 딱 1회
            // 시도 후 곧장 에러 로그로 끝나 버린다.
            _retryAttempt = 0;
            CancelScheduledRetry();
            RequestLoad();
        }

        // 어댑터 로드 호출은 반드시 이 한 곳을 거친다. 로드가 이미 진행 중이면 건너뛴다 —
        // AppLovin MAX 등은 같은 광고 단위에 중복 Load를 걸면 경고를 찍고 무시하는데,
        // show → NotReady → 대기 → show로 폴링하는 호출 패턴에서 매 시도마다 어댑터를
        // 다시 부르면 그 경고가 반복된다. IsReady를 함께 보는 이유: 어댑터가 Loaded를
        // 발화시키지 않고도 준비 상태가 될 가능성에 대비한 보험이다 — 플래그 하나만 보면
        // Loaded가 안 오는 순간 영원히 걸어잠긴다.
        private void RequestLoad()
        {
            if (_isLoadInFlight && !_adapter.IsReady) return;

            _isLoadInFlight = true;
            _adapter.Load();
        }

        public Awaitable<AdShowResult> ShowAsync(string placement = null)
        {
            if (_isDisposed) return Immediate(AdShowResult.Failed(new AdError(-1, "서비스가 이미 해제됐다")));

            // 순서가 중요하다. 광고제거는 로드조차 트리거하지 않아야 하므로 가장 먼저 본다.
            if (_blockWhenAdsRemoved && _adsRemoved()) return Immediate(AdShowResult.Blocked());

            if (_showCompletion != null) return Immediate(AdShowResult.Failed(new AdError(-2, "이미 표시 중이다")));

            // 쿨다운은 표시 시점에 시작된다(OnDisplayed). 재진입 가드 다음, NotReady 가드
            // 앞에 둔다 — 차단된 호출은 로드조차 트리거하지 않아야 한다.
            if (_isCoolingDown) return Immediate(AdShowResult.Blocked());

            if (!_adapter.IsReady)
            {
                Load();   // 다음 기회를 위해 미리 채워둔다
                return Immediate(AdShowResult.NotReady());
            }

            _activePlacement = placement;
            _showCompletion = new AwaitableCompletionSource<AdShowResult>();
            _pendingReward = null;

            // 이전 쇼의 늦은 Closed가 예약돼 있으면 새 쇼를 확정시켜 버린다. 함께 버린다.
            _scheduledClose?.Dispose();
            _scheduledClose = null;

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
            _isLoadInFlight = false;
            _retryAttempt = 0;
            Loaded?.Invoke();
        }

        private void OnLoadFailed(AdError error)
        {
            _isLoadInFlight = false;
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
                // Load()가 아니라 RequestLoad()를 직접 부른다 — 재시도는 _retryAttempt를
                // 리셋하면 안 되므로 Load()를 거치지 않는다(그 이유는 위 Load() 주석 참고).
                if (!_isDisposed) RequestLoad();
            });
        }

        private void CancelScheduledRetry()
        {
            _scheduledRetry?.Dispose();
            _scheduledRetry = null;
        }

        private void OnDisplayed()
        {
            StartCooldown();
            Displayed?.Invoke();
        }

        // 쿨다운은 요청 시점도 닫힘 시점도 아니라 표시 시점에 시작한다 — "마지막으로 실제
        // 유저 화면에 뜬 순간"을 기준으로 다음 광고까지의 최소 간격을 두는 것이 정책의 의도다.
        // 보상형은 여기 오지 않게 하는 게 아니라 cooldownSeconds를 0으로 조립해 무력화한다
        // (AdService.BuildAdUnits 참고) — 이 클래스 자신은 포맷을 판단해 게이트를 켜고 끄지 않는다.
        private void StartCooldown()
        {
            if (_cooldownSeconds <= 0f) return;

            _isCoolingDown = true;
            _scheduledCooldown?.Dispose();
            _scheduledCooldown = _dispatcher.Delay(_cooldownSeconds, () =>
            {
                _scheduledCooldown = null;
                _isCoolingDown = false;
            });
        }

        private void OnDisplayFailed(AdError error)
        {
            Debug.LogWarning($"[AdService] {_format} 표시 실패: {error}");

            // 이미 완료된 쇼에 대한 중복/지연 DisplayFailed면 재로드하지 않는다.
            // FinalizeClose와 같은 보호 — 중복 로드는 세 SDK 모두 에러로 취급한다.
            if (!Complete(AdShowResult.Failed(error))) return;

            // 표시 실패는 대개 만료되거나 소진된 광고가 원인이다. 새로 받아온다.
            Load();
        }
        // 보상은 래치만 한다. 여기서 완료시키면, 보상 후 닫힘 사이에 유저가 앱을 떠나는
        // 경우와 닫힘이 먼저 오는 경우를 구분할 수 없게 된다.
        private void OnRewarded(AdReward reward)
        {
            _pendingReward = reward;
        }

        private void OnClosed()
        {
            if (_isDisposed) return;

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

            // 세 SDK 모두 "닫히면 즉시 다음 광고를 로드하라"고 권고한다.
            // 로드에 수 초가 걸리므로 여기서 시작하지 않으면 다음 기회를 놓친다.
            // Complete가 false를 반환하면(중복 Closed) 이 지점에 도달하지 않으므로
            // 중복 로드가 발생하지 않는다.
            Load();
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
            _scheduledCooldown?.Dispose();
            _scheduledCooldown = null;

            try
            {
                // spec: 해제 시 대기 중인 ShowAsync를 Failed로 깨운다. 그러지 않으면 호출자가 영구 정지한다.
                // SetResult는 대기 중이던 호출자의 이어달리기를 동기적으로 재개시킬 수 있다 —
                // 그 이어달리기가 예외를 던지면 finally 없이는 아래 구독 해제와 어댑터 Dispose가
                // 통째로 건너뛰어져 SDK 광고 객체가 샌다.
                Complete(AdShowResult.Failed(new AdError(-4, "서비스가 해제됐다")));
            }
            finally
            {
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
}
