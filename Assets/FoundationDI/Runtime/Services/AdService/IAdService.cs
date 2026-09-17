using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IAdService : IDisposable
    {
        bool IsInitialized { get; }
        Awaitable<bool> InitializeAsync();

        IInterstitialAd Interstitial { get; }
        IRewardedAd Rewarded { get; }
        IBannerAd Banner { get; }
        IAdConsent Consent { get; }

        // iOS 14+ 추적 권한(ATT). InitializeAsync가 provider 초기화 앞에서 한 번 묻는다
        // (AdServiceSettings의 Request Tracking Authorization으로 끌 수 있다).
        // 게임이 직접 시점을 고르고 싶으면 그 설정을 끄고 여기서 RequestAsync를 부른다.
        ITrackingAuthorization TrackingAuthorization { get; }

        // 인앱 구매로 광고를 제거한 상태. 전면·배너는 차단되고 보상형은 계속 동작한다.
        bool AdsRemoved { get; set; }

        event Action<AdFormat> Loaded;
        event Action<AdFormat> Displayed;
        event Action<AdFormat> Closed;
        event Action<AdImpression> Paid;
        event Action<bool> AdsRemovedChanged;
    }
}
