using System;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고 단위 하나를 나타내는 얇은 MAX 래퍼. 두 포맷을 한 클래스로 묶은 이유는
    // MAX 쪽에서 둘이 정말로 콜백 그룹과 Load/Show/IsReady 호출만 다른 쌍둥이이기 때문이다
    // (MaxSdkCallbacks.Interstitial vs .Rewarded, MaxSdk.LoadInterstitial vs .LoadRewardedAd 등) —
    // 필터링·구독 해제·Dispose 안전성 같은 나머지 배선은 완전히 동일하다. 포맷별로 클래스를
    // 쪼갰다면 그 나머지 배선이 통째로 복붙됐을 것이다.
    //
    // MAX의 콜백은 전역 정적 이벤트라 앱 안의 "모든" 전면/보상 광고 단위에 대해 발화한다.
    // 그래서 모든 핸들러가 adUnitId를 이 인스턴스가 생성될 때 받은 값과 대조해 걸러낸다 —
    // 걸러내지 않으면 서로 다른 광고 단위끼리 이벤트가 뒤섞인다.
    public class AppLovinFullScreenAdapter : IFullScreenAdapter
    {
        private readonly AdFormat _format;
        private readonly string _adUnitId;

        private bool _isDisposed;

        public event Action Loaded;
        public event Action<AdError> LoadFailed;
        public event Action Displayed;
        public event Action<AdError> DisplayFailed;
        public event Action Closed;
        public event Action<AdReward> Rewarded;
        public event Action<AdImpression> Paid;

        public AppLovinFullScreenAdapter(AdFormat format, string adUnitId)
        {
            _format = format;
            _adUnitId = adUnitId;

            if (_format == AdFormat.Interstitial)
            {
                MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnAdLoaded;
                MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnAdLoadFailed;
                MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnAdDisplayed;
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnAdDisplayFailed;
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnAdHidden;
                MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnAdRevenuePaid;
            }
            else
            {
                MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnAdLoaded;
                MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnAdLoadFailed;
                MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnAdDisplayed;
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnAdDisplayFailed;
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnAdHidden;
                MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnAdReceivedReward;
                MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnAdRevenuePaid;
            }
        }

        // 상태를 직접 들고 있지 않고 MAX에 그대로 위임한다 — MAX 스스로가 이 광고 단위의
        // 준비 상태를 추적하므로 여기서 Loaded/Show로 플래그를 갱신하며 이중 관리할 이유가
        // 없고, 그러면 두 상태가 어긋날 여지도 원천 차단된다.
        public bool IsReady => !_isDisposed && (_format == AdFormat.Interstitial
            ? MaxSdk.IsInterstitialReady(_adUnitId)
            : MaxSdk.IsRewardedAdReady(_adUnitId));

        // 진행 중 로드 중복 호출 가드는 FullScreenAdUnit(정책 계층)의 책임이라 여기서는
        // 걸지 않는다(README 5.8절).
        public void Load()
        {
            if (_isDisposed) return;

            if (_format == AdFormat.Interstitial) MaxSdk.LoadInterstitial(_adUnitId);
            else MaxSdk.LoadRewardedAd(_adUnitId);
        }

        public void Show()
        {
            if (_isDisposed) return;

            if (_format == AdFormat.Interstitial) MaxSdk.ShowInterstitial(_adUnitId);
            else MaxSdk.ShowRewardedAd(_adUnitId);
        }

        private void OnAdLoaded(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Loaded?.Invoke();
        }

        private void OnAdLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo error)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            LoadFailed?.Invoke(error.ToAdError());
        }

        private void OnAdDisplayed(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Displayed?.Invoke();
        }

        private void OnAdDisplayFailed(string adUnitId, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;

            // 표시 실패 뒤에는 MAX가 OnAdHiddenEvent를 보내지 않는다. Closed를 합성하지
            // 않는다 — FullScreenAdUnit.OnDisplayFailed가 DisplayFailed 하나만으로 대기 중인
            // ShowAsync를 Failed로 완료시킨다(정책 계층 책임, README/작업 지시서 참고).
            DisplayFailed?.Invoke(error.ToAdError());
        }

        private void OnAdHidden(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Closed?.Invoke();
        }

        private void OnAdReceivedReward(string adUnitId, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Rewarded?.Invoke(new AdReward(reward.Label, reward.Amount));
        }

        private void OnAdRevenuePaid(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Paid?.Invoke(info.ToAdImpression(_format));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 정적 이벤트라 여기서 해제하지 않으면 이 어댑터가 계속 참조되고, 이후 다른
            // 광고 단위(또는 같은 단위의 재구독)에서 오는 이벤트까지 이중 발화한다.
            if (_format == AdFormat.Interstitial)
            {
                MaxSdkCallbacks.Interstitial.OnAdLoadedEvent -= OnAdLoaded;
                MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent -= OnAdLoadFailed;
                MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent -= OnAdDisplayed;
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= OnAdDisplayFailed;
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent -= OnAdHidden;
                MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent -= OnAdRevenuePaid;
            }
            else
            {
                MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= OnAdLoaded;
                MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= OnAdLoadFailed;
                MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent -= OnAdDisplayed;
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= OnAdDisplayFailed;
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= OnAdHidden;
                MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= OnAdReceivedReward;
                MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent -= OnAdRevenuePaid;
            }

            Loaded = null;
            LoadFailed = null;
            Displayed = null;
            DisplayFailed = null;
            Closed = null;
            Rewarded = null;
            Paid = null;
        }
    }
}
