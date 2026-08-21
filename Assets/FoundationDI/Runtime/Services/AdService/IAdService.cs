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

        // 인앱 구매로 광고를 제거한 상태. 전면·배너는 차단되고 보상형은 계속 동작한다.
        bool AdsRemoved { get; set; }

        event Action<AdFormat> Loaded;
        event Action<AdFormat> Displayed;
        event Action<AdFormat> Closed;
        event Action<AdImpression> Paid;
        event Action<bool> AdsRemovedChanged;
    }
}
