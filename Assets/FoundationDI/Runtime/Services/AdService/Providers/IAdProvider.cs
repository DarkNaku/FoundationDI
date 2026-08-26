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

        // 전역/미매칭 임프레션 경로. 임프레션 데이터가 광고 객체가 아니라 SDK 전역 이벤트
        // 하나로만 오는 SDK를 위한 seam이다 — 그런 SDK에서는 어댑터별 Paid만으로는 특히
        // 배너 자동 갱신 수익이 어떤 어댑터에도 매칭되지 않아 조용히 누락된다.
        //
        // **현재 구현된 세 provider(Dummy/AppLovin/LevelPlay) 중 이 경로를 쓰는 것은 없다.**
        // 원래 이 주석은 LevelPlay를 그 사례로 지목했지만, LevelPlay 9.5.1은 전면·보상·배너
        // 각 광고 객체가 자기 OnAdImpressionDataReady를 갖고 있고 전역
        // LevelPlay.OnImpressionDataReady는 [Obsolete]다 — 그래서 LevelPlay 어댑터도 어댑터별
        // Paid로만 흘린다(LevelPlayAdProvider의 ImpressionPaid 주석 참고). 한 임프레션이 두
        // 경로로 올라오면 수익이 이중 계상되므로 새 어댑터는 반드시 둘 중 하나만 고른다.
        event Action<AdImpression> ImpressionPaid;
    }
}
