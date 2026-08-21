using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum BannerPosition { Bottom, Top }
    public enum BannerSize { Standard, Large, MediumRectangle, Leaderboard, Adaptive }

    public readonly struct BannerOptions
    {
        public BannerPosition Position { get; }
        public BannerSize Size { get; }
        public bool UseAdaptive { get; }

        public BannerOptions(BannerPosition position, BannerSize size, bool useAdaptive)
        {
            Position = position;
            Size = size;
            UseAdaptive = useAdaptive;
        }
    }

    // provider가 초기화에 필요로 하는 것만 담는다. AdServiceSettings 전체를 넘기지 않는 이유는
    // 재시도·유예 프레임 같은 정책값이 상위 계층 소관이라 provider가 볼 이유가 없기 때문이다.
    public readonly struct AdProviderContext
    {
        public string AppKey { get; }       // LevelPlay appKey / MAX sdkKey. AdMob은 불필요(null)
        public bool VerboseLogging { get; }
        public bool TestMode { get; }
        public IReadOnlyList<string> TestDeviceIds { get; }

        public AdProviderContext(string appKey, bool verboseLogging, bool testMode,
                                 IReadOnlyList<string> testDeviceIds)
        {
            AppKey = appKey;
            VerboseLogging = verboseLogging;
            TestMode = testMode;
            TestDeviceIds = testDeviceIds;
        }
    }

    public interface IAdProvider : IDisposable
    {
        string Name { get; }
        Awaitable<bool> InitializeAsync(AdProviderContext context);
        IAdConsent Consent { get; }

        IFullScreenAdapter CreateInterstitial(string adUnitId);
        IFullScreenAdapter CreateRewarded(string adUnitId);
        IBannerAdapter CreateBanner(string adUnitId, BannerOptions options);

        // 전역/미매칭 임프레션 경로. LevelPlay는 임프레션 데이터가 광고 객체가 아니라
        // SDK 전역 이벤트 하나로 오기 때문에 어댑터별 Paid만으로는 배너 갱신 수익이 누락된다.
        event Action<AdImpression> ImpressionPaid;
    }
}
