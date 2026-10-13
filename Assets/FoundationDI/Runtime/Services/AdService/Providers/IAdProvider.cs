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

        // ATT 응답. provider가 이것을 자기 SDK의 프라이버시 세터로 번역한다.
        //
        // IAdProvider의 별도 메서드가 아니라 context에 실은 이유는 순서 때문이다. LevelPlay·MAX·
        // AdMob 모두 프라이버시 세터를 SDK Init **이전에** 불러야 효력이 있는데, 별도 메서드로
        // 두면 그 규약이 문서로만 존재해 언제든 깨진다. 초기화 인자로 두면 provider가 이 값을
        // 받는 시점이 곧 Init 직전이라 순서를 틀릴 수가 없다.
        public TrackingAuthorizationStatus TrackingStatus { get; }

        // 추적이 명시적으로 막혔는가. provider는 이 값 하나만 보고 분기한다.
        //
        // NotDetermined가 false인 것이 핵심이다. 그 값은 "iOS인데 아직 안 물었다"와 "ATT가
        // 없는 플랫폼(안드로이드·에디터)"을 함께 뜻하는데, 후자가 압도적으로 흔하다
        // (UnsupportedTrackingAuthorization.Status는 항상 NotDetermined다). 제한으로 해석하면
        // 안드로이드 광고가 통째로 비개인화로 떨어져 수익이 이유 없이 죽는다.
        //
        // iOS에서 "아직 안 물었다"가 provider까지 도달하는 경로는
        // ConsentServiceSettings의 Request Tracking Authorization을 끈 경우뿐이고, 그건 게임이 직접 묻겠다고
        // 선언한 경우라 판단 주체가 게임이다.
        public bool IsTrackingRestricted =>
            TrackingStatus == TrackingAuthorizationStatus.Denied ||
            TrackingStatus == TrackingAuthorizationStatus.Restricted;

        public AdProviderContext(string appKey, bool verboseLogging, bool testMode,
                                 IReadOnlyList<string> testDeviceIds,
                                 TrackingAuthorizationStatus trackingStatus =
                                     TrackingAuthorizationStatus.NotDetermined)
        {
            AppKey = appKey;
            VerboseLogging = verboseLogging;
            TestMode = testMode;
            TestDeviceIds = testDeviceIds;
            TrackingStatus = trackingStatus;
        }

        // 설정에서 만들어진 context는 ATT를 묻기 전에 조립되므로 추적 상태를 모른다.
        // AdService가 응답을 받은 뒤 이 메서드로 채워 provider에 넘긴다.
        public AdProviderContext WithTrackingStatus(TrackingAuthorizationStatus status) =>
            new(AppKey, VerboseLogging, TestMode, TestDeviceIds, status);
    }

    public interface IAdProvider : IDisposable
    {
        string Name { get; }
        Awaitable<bool> InitializeAsync(AdProviderContext context);

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
