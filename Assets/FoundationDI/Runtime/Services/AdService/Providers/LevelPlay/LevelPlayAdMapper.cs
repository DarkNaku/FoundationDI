using System;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // LevelPlay SDK 타입 -> FoundationDI 타입 변환만 담당한다. SDK 타입을 아는 지점을 여기
    // 하나로 모아, 어댑터 쪽 콜백 핸들러가 매핑 규칙을 반복하지 않게 한다(AppLovinAdMapper와
    // 같은 역할).
    internal static class LevelPlayAdMapper
    {
        // AdImpression.AdPlatform으로 나가 Firebase의 ad_platform이 된다.
        private const string Platform = "LevelPlay";

        // LevelPlay의 ILRD(impression level revenue data)는 항상 USD다. impressionData에
        // 통화 필드 자체가 없다(LevelPlayImpressionData의 프로퍼티 목록 참고) — AdMob처럼
        // 퍼블리셔 통화가 섞일 수 있는 SDK를 위해 AdImpression이 Currency를 들고 있을 뿐,
        // 이 어댑터 입장에서는 상수다.
        private const string Currency = "USD";

        // Android의 dp 정의. LevelPlayAdSize.Width/Height는 Android에서는 dp,
        // iOS에서는 point 단위다(LevelPlayAdSize.CreateAdaptiveAdSize의 XML 주석에 명시).
        private const float DpPerInchBase = 160f;

        public static AdError ToAdError(this LevelPlayAdError error)
        {
            // LevelPlayAdError는 JSON 파싱 실패 시 모든 필드를 기본값으로 둔 채 돌아온다
            // (LevelPlayAdError.cs의 catch 분기) — null 자체는 오지 않지만 방어한다.
            if (error == null) return new AdError(0, "알 수 없는 LevelPlay 오류");

            return new AdError(error.ErrorCode, error.ErrorMessage);
        }

        // LevelPlayReward.Amount는 int다. AdReward.Amount(double)로 넓히기만 하면 된다.
        public static AdReward ToAdReward(this LevelPlayReward reward)
        {
            if (reward == null) return new AdReward(string.Empty, 0d);

            return new AdReward(reward.Name, reward.Amount);
        }

        // 수익 필터링(Revenue가 없거나 0 이하)은 여기서 하지 않는다 — 어댑터가 발화 직전에
        // 판단한다(AppLovin 어댑터와 같은 배치). 이 함수는 변환만 한다.
        public static AdImpression ToAdImpression(this LevelPlayImpressionData data, AdFormat format)
        {
            return new AdImpression(
                format,
                Platform,
                data.AdNetwork,

                // AdImpression.AdUnitId의 문서화된 소비처는 Firebase의 ad_unit_name이다.
                // LevelPlay는 ID와 사람이 읽는 이름을 둘 다 주므로 이름을 우선한다 — MAX는
                // 이름 필드가 없어 AppLovin 어댑터가 불투명한 ID를 넣을 수밖에 없었던 것과
                // 다르다. 이름이 비어 있으면 ID로 떨어뜨려 최소한 식별은 되게 한다.
                string.IsNullOrEmpty(data.MediationAdUnitName)
                    ? data.MediationAdUnitId
                    : data.MediationAdUnitName,

                // NetworkPlacement는 "실제로 채운 네트워크 쪽 배치"다. LevelPlay에서는
                // instanceName이 그 자리다(AdImpression.NetworkPlacement 주석의 서술과 일치).
                data.InstanceName,

                // 게임이 ShowAsync에 넘긴 배치명은 정책 계층(FullScreenAdUnit/BannerAdUnit)이
                // 스탬프한다. impressionData에도 Placement가 있지만 그건 LevelPlay 대시보드의
                // 배치라 의미가 다르다 — 섞으면 두 개념이 한 필드에서 뒤엉킨다.
                null,

                data.Revenue ?? 0d,
                Currency,
                MapRevenuePrecision(data.Precision),

                // AdImpression.CreativeId의 계약은 "없으면 null"이다.
                // LevelPlayImpressionData는 키가 없으면 null을 주지만 빈 문자열이 실려 올 수
                // 있어 함께 정규화한다.
                string.IsNullOrEmpty(data.CreativeId) ? null : data.CreativeId);
        }

        // LevelPlay ILRD의 precision 값은 SDK 소스에 상수로 박혀 있지 않고 네이티브가 내려주는
        // 문자열을 그대로 흘린다(LevelPlayImpressionData.Precision은 JSON 딕셔너리 조회다).
        // ironSource ILRD 문서가 정의하는 값은 bid / pred / exact / undisclosed 넷이다.
        //   - exact : 실제 정산 단가            -> Exact
        //   - bid   : 실시간 입찰가. 그 임프레션에 대해서는 확정값이다 -> Exact
        //   - pred  : 과거 데이터 기반 예측치    -> Estimated
        //   - undisclosed : 네트워크가 공개하지 않음 -> Unknown
        // AppLovin이 쓰는 표기(estimated / publisher_defined)도 함께 받아 준다 — 비용이 없고,
        // LevelPlay가 향후 표기를 통일할 경우 조용히 Unknown으로 떨어지는 것을 막는다.
        // 알려지지 않은 값은 크래시 대신 Unknown이다.
        internal static AdRevenuePrecision MapRevenuePrecision(string precision)
        {
            if (string.IsNullOrEmpty(precision)) return AdRevenuePrecision.Unknown;

            if (string.Equals(precision, "exact", StringComparison.OrdinalIgnoreCase)) return AdRevenuePrecision.Exact;
            if (string.Equals(precision, "bid", StringComparison.OrdinalIgnoreCase)) return AdRevenuePrecision.Exact;
            if (string.Equals(precision, "pred", StringComparison.OrdinalIgnoreCase)) return AdRevenuePrecision.Estimated;
            if (string.Equals(precision, "estimated", StringComparison.OrdinalIgnoreCase)) return AdRevenuePrecision.Estimated;

            if (string.Equals(precision, "publisher_defined", StringComparison.OrdinalIgnoreCase))
                return AdRevenuePrecision.PublisherDefined;

            return AdRevenuePrecision.Unknown;
        }

        // BannerOptions -> LevelPlayAdSize.
        //
        // MAX 어댑터와 달리 다섯 크기를 전부 낼 수 있다. LevelPlay는 MREC도 같은
        // LevelPlayBannerAd 객체로 다루기 때문이다(MAX는 CreateMRec이라는 별도 API라
        // AppLovinBannerAdapter가 Large/MediumRectangle/Leaderboard를 경고하고 버렸다).
        public static LevelPlayAdSize ToLevelPlayAdSize(this BannerOptions options)
        {
            // UseAdaptive는 Size와 별개의 불리언이라 둘이 어긋날 수 있다. 적응형이 더 구체적인
            // 의도이므로 어느 쪽이든 켜져 있으면 적응형으로 만든다. customWidth를 주지 않으면
            // SDK가 화면 폭을 쓴다.
            if (options.UseAdaptive || options.Size == BannerSize.Adaptive)
            {
                return LevelPlayAdSize.CreateAdaptiveAdSize();
            }

            switch (options.Size)
            {
                case BannerSize.Large: return LevelPlayAdSize.LARGE;
                case BannerSize.MediumRectangle: return LevelPlayAdSize.MEDIUM_RECTANGLE;
                case BannerSize.Leaderboard: return LevelPlayAdSize.LEADERBOARD;
                default: return LevelPlayAdSize.BANNER;
            }
        }

        // BannerPosition은 상/하 두 값뿐이라 LevelPlay의 9방향 중 중앙 정렬 둘로만 매핑한다.
        public static LevelPlayBannerPosition ToLevelPlayPosition(this BannerPosition position)
        {
            return position == BannerPosition.Top
                ? LevelPlayBannerPosition.TopCenter
                : LevelPlayBannerPosition.BottomCenter;
        }

        // LevelPlayAdSize.Height(dp/point) -> IBannerAdapter.Height(화면 픽셀).
        //
        // 이 환산식(Screen.dpi / 160)은 LevelPlay 패키지 자신이 에디터 배너 목업에서 쓰는 것과
        // 같다(Runtime/Platforms/Editor/EditorAds/Scripts/BannerPrefab.cs:171). Android에서는
        // dp의 정의 그대로라 정확하고, iOS에서는 point -> pixel 배율이 기기별 nativeScale(2 또는 3)
        // 이라 근사다 — Unity가 그 배율을 노출하지 않으므로 SDK와 같은 근사를 쓴다.
        //
        // Screen.dpi는 알 수 없는 기기에서 0을 돌려준다. 그때 0을 곱하면 배너가 없는 것과
        // 구분되지 않으므로 배율 1(값을 픽셀로 간주)로 떨어뜨린다.
        internal static float DpToPixels(float dp)
        {
            var dpi = Screen.dpi;
            var scale = dpi > 0f ? dpi / DpPerInchBase : 1f;
            return dp * scale;
        }
    }
}
