using System;
using Unity.Services.LevelPlay;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고 단위 하나를 나타내는 얇은 LevelPlay 래퍼.
    //
    // 두 포맷을 한 클래스로 묶은 것은 AppLovinFullScreenAdapter와 같은 판단이다. LevelPlay
    // 쪽에서 ILevelPlayInterstitialAd와 ILevelPlayRewardedAd는 OnAdRewarded/GetReward 둘을
    // 빼면 완전히 같은 모양이고(LoadAd/ShowAd/DestroyAd/IsAdReady + 같은 이름의 콜백들),
    // 나머지 배선(스레드 마샬링, 수익 필터, Dispose 안전성)은 포맷과 무관하게 동일하다.
    // 두 클래스로 쪼갰다면 그 배선이 통째로 복붙됐을 것이다. 두 SDK 인터페이스에 공통 조상이
    // 없어 필드를 둘 두고 분기하지만, 분기는 생성자/Load/Show/IsReady/Dispose 다섯 곳뿐이다.
    //
    // MAX와 달리 콜백이 전역 정적 이벤트가 아니라 광고 객체 인스턴스의 이벤트라서,
    // adUnitId로 남의 광고 단위 이벤트를 걸러낼 필요가 없다.
    public class LevelPlayFullScreenAdapter : IFullScreenAdapter
    {
        private readonly AdFormat _format;
        private readonly IAdDispatcher _dispatcher;

        // 둘 중 하나만 채워진다. _format이 어느 쪽인지 결정한다.
        private readonly ILevelPlayInterstitialAd _interstitial;
        private readonly ILevelPlayRewardedAd _rewarded;

        // OnAdImpressionDataReady는 메인 스레드 보장이 없다(아래 OnImpressionDataReady 주석
        // 참고). 그 핸들러가 이 플래그를 읽으므로 volatile이어야 한다.
        private volatile bool _isDisposed;

        public event Action Loaded;
        public event Action<AdError> LoadFailed;
        public event Action Displayed;
        public event Action<AdError> DisplayFailed;
        public event Action Closed;
        public event Action<AdReward> Rewarded;
        public event Action<AdImpression> Paid;

        public LevelPlayFullScreenAdapter(AdFormat format, string adUnitId, IAdDispatcher dispatcher)
        {
            _format = format;
            _dispatcher = dispatcher;

            if (format == AdFormat.Interstitial)
            {
                _interstitial = new LevelPlayInterstitialAd(adUnitId);

                _interstitial.OnAdLoaded += OnAdLoaded;
                _interstitial.OnAdLoadFailed += OnAdLoadFailed;
                _interstitial.OnAdDisplayed += OnAdDisplayed;
                _interstitial.OnAdDisplayFailed += OnAdDisplayFailed;
                _interstitial.OnAdClosed += OnAdClosed;
                _interstitial.OnAdImpressionDataReady += OnImpressionDataReady;
            }
            else
            {
                _rewarded = new LevelPlayRewardedAd(adUnitId);

                _rewarded.OnAdLoaded += OnAdLoaded;
                _rewarded.OnAdLoadFailed += OnAdLoadFailed;
                _rewarded.OnAdDisplayed += OnAdDisplayed;
                _rewarded.OnAdDisplayFailed += OnAdDisplayFailed;
                _rewarded.OnAdClosed += OnAdClosed;
                _rewarded.OnAdRewarded += OnAdRewarded;
                _rewarded.OnAdImpressionDataReady += OnImpressionDataReady;
            }
        }

        // 상태를 직접 들고 있지 않고 SDK에 위임한다 — SDK가 이 광고 객체의 준비 상태를
        // 스스로 추적하므로 여기서 Loaded/Show로 플래그를 갱신하며 이중 관리할 이유가 없고,
        // 그러면 두 상태가 어긋날 여지도 사라진다(AppLovin 어댑터와 같은 이유).
        public bool IsReady
        {
            get
            {
                if (_isDisposed) return false;

                return _format == AdFormat.Interstitial
                    ? _interstitial.IsAdReady()
                    : _rewarded.IsAdReady();
            }
        }

        // 진행 중 로드 중복 호출 가드는 FullScreenAdUnit(정책 계층)의 책임이라 여기서는
        // 걸지 않는다.
        public void Load()
        {
            if (_isDisposed) return;

            if (_format == AdFormat.Interstitial) _interstitial.LoadAd();
            else _rewarded.LoadAd();
        }

        // LevelPlay의 ShowAd는 배치명을 옵션 인자로 받지만, IFullScreenAdapter.Show()에는
        // 배치명이 없다 — 배치명을 아는 것은 정책 계층(ShowAsync 인자)뿐이고, 그 계층은
        // 배치명을 임프레션에 스탬프하는 용도로만 쓴다. 여기서 넘길 값이 없으므로 생략한다
        // (null이면 SDK가 기본 배치를 쓴다).
        public void Show()
        {
            if (_isDisposed) return;

            if (_format == AdFormat.Interstitial) _interstitial.ShowAd();
            else _rewarded.ShowAd();
        }

        // 아래 다섯(+보상) 콜백은 메인 스레드에서 온다. LevelPlay 9.5.1은 광고 수명주기
        // 콜백을 플랫폼 계층에서 이미 Unity 동기화 컨텍스트로 밀어 넣기 때문이다 —
        // Android는 AndroidJavaProxy 리스너가 전부 ThreadUtil.Post로 감싸고
        // (Runtime/Platforms/Android/UnityInterstitialAdListener.cs,
        //  UnityRewardedAdListener.cs), iOS도 마찬가지다
        // (Runtime/Platforms/iOS/IosInterstitialAd.cs:70~100, IosRewardedAd.cs:82~117).
        // ThreadUtil은 BeforeSceneLoad에서 캡처한 SynchronizationContext에 Post한다
        // (Runtime/Platforms/ThreadUtil.cs). 그래서 이 경로들만은 dispatcher로 다시 감싸지
        // 않는다 — 한 프레임을 더 미루면 정책 계층의 보상 유예 프레임 계산
        // (FullScreenAdUnit.OnClosed)에 불필요한 지연이 얹힌다.
        private void OnAdLoaded(LevelPlayAdInfo info)
        {
            if (_isDisposed) return;
            Loaded?.Invoke();
        }

        private void OnAdLoadFailed(LevelPlayAdError error)
        {
            if (_isDisposed) return;
            LoadFailed?.Invoke(error.ToAdError());
        }

        private void OnAdDisplayed(LevelPlayAdInfo info)
        {
            if (_isDisposed) return;
            Displayed?.Invoke();
        }

        // 표시 실패 뒤에 OnAdClosed가 오는지는 네트워크마다 다르다. Closed를 합성하지 않는다 —
        // FullScreenAdUnit.OnDisplayFailed가 DisplayFailed 하나만으로 대기 중인 ShowAsync를
        // Failed로 완료시키고 재로드까지 건다(정책 계층 책임).
        private void OnAdDisplayFailed(LevelPlayAdInfo info, LevelPlayAdError error)
        {
            if (_isDisposed) return;
            DisplayFailed?.Invoke(error.ToAdError());
        }

        private void OnAdClosed(LevelPlayAdInfo info)
        {
            if (_isDisposed) return;
            Closed?.Invoke();
        }

        // 보상 모드에서만 구독한다. 전면 모드에서는 이 핸들러가 아예 걸리지 않으므로
        // IFullScreenAdapter의 "전면 어댑터는 Rewarded를 발화시키지 않는다" 계약이 구조적으로
        // 지켜진다.
        private void OnAdRewarded(LevelPlayAdInfo info, LevelPlayReward reward)
        {
            if (_isDisposed) return;
            Rewarded?.Invoke(reward.ToAdReward());
        }

        // **이 콜백만은 메인 스레드가 아니다.** SDK가 인터페이스 XML 주석에 명시한다:
        // "This event is triggered on a background thread, not the Unity main thread."
        // (Runtime/Api/ILevelPlayInterstitialAd.cs:53, ILevelPlayRewardedAd.cs:53)
        // 소스로도 확인된다 — 수명주기 콜백과 달리 Android의 임프레션 리스너는 ThreadUtil을
        // 거치지 않고 Java 프록시 스레드에서 그대로 올라온다
        // (Runtime/Platforms/Android/UnityLevelPlayImpressionDataListener.cs의
        //  onImpressionSuccess는 m_Listener.onImpressionSuccess를 직접 호출한다.
        //  같은 파일의 UnityInterstitialAdListener는 전부 ThreadUtil.Post로 감싸는 것과 대비된다).
        // iOS는 ThreadUtil.Post로 감싸긴 하지만(IosInterstitialAd.cs:105) 문서가 보장하지
        // 않으므로 둘 다 마샬링한다.
        //
        // IFullScreenAdapter의 계약은 "모든 이벤트를 메인 스레드에서 발화"이고, 그 위의
        // FullScreenAdUnit.OnPaid는 non-volatile 필드(_activePlacement)를 읽는다. 그래서
        // IAdDispatcher.Post로 넘긴다 — AppLovin이 MaxSdkBase.InvokeEventsOnUnityMainThread로
        // SDK에게 마샬링을 지시할 수 있었던 것과 달리, LevelPlay에는 그런 스위치가 없어
        // 이 seam에서 직접 처리하는 것 외에 방법이 없다.
        //
        // 변환(ToAdImpression)은 Post 밖에서 미리 끝낸다. LevelPlayImpressionData는 생성자에서
        // 이미 파싱을 마친 읽기 전용 딕셔너리라 백그라운드에서 읽어도 안전하고, 이렇게 해 두면
        // SDK 객체를 메인 스레드까지 끌고 가지 않는다.
        private void OnImpressionDataReady(LevelPlayImpressionData data)
        {
            if (_isDisposed || data == null) return;

            // 수익이 없는 임프레션은 흘리지 않는다. LevelPlay는 값 자체를 안 실어 보내면
            // Revenue가 null이다. 그대로 0으로 흘려보내면 임프레션을 합산하는 소비자가
            // "매출 0"을 계상해 평균 단가가 오염된다(AppLovin 어댑터가 -1 센티널을 걸러내는
            // 것과 같은 판단).
            var revenue = data.Revenue;
            if (!revenue.HasValue || revenue.Value <= 0d) return;

            var impression = data.ToAdImpression(_format);

            _dispatcher.Post(() =>
            {
                if (_isDisposed) return;
                Paid?.Invoke(impression);
            });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 광고 객체 인스턴스의 이벤트지만 구독을 남기면 DestroyAd 이후에 도착하는
            // 지연 콜백이 이미 해제된 어댑터를 계속 살려 둔다. _isDisposed 가드가 있어도
            // 구독은 반드시 끊는다.
            if (_format == AdFormat.Interstitial)
            {
                _interstitial.OnAdLoaded -= OnAdLoaded;
                _interstitial.OnAdLoadFailed -= OnAdLoadFailed;
                _interstitial.OnAdDisplayed -= OnAdDisplayed;
                _interstitial.OnAdDisplayFailed -= OnAdDisplayFailed;
                _interstitial.OnAdClosed -= OnAdClosed;
                _interstitial.OnAdImpressionDataReady -= OnImpressionDataReady;

                _interstitial.DestroyAd();
            }
            else
            {
                _rewarded.OnAdLoaded -= OnAdLoaded;
                _rewarded.OnAdLoadFailed -= OnAdLoadFailed;
                _rewarded.OnAdDisplayed -= OnAdDisplayed;
                _rewarded.OnAdDisplayFailed -= OnAdDisplayFailed;
                _rewarded.OnAdClosed -= OnAdClosed;
                _rewarded.OnAdRewarded -= OnAdRewarded;
                _rewarded.OnAdImpressionDataReady -= OnImpressionDataReady;

                _rewarded.DestroyAd();
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
