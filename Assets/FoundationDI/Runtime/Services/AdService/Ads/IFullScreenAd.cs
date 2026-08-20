using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고의 공통 계약. 게임 코드는 IAdService.Interstitial / .Rewarded 로 접근한다.
    public interface IFullScreenAd
    {
        bool IsReady { get; }

        // 수동 로드. 자동 재로드가 기본이라 평소에는 부를 일이 없다.
        void Load();

        // placement는 분석 이벤트에 실릴 배치명이며 광고 표시 자체에는 영향을 주지 않는다.
        Awaitable<AdShowResult> ShowAsync(string placement = null);
    }

    // 현재 IFullScreenAd와 동일하지만 호출부 타입 안전성과 향후 분화를 위해 분리한다.
    public interface IInterstitialAd : IFullScreenAd { }
    public interface IRewardedAd : IFullScreenAd { }
}
