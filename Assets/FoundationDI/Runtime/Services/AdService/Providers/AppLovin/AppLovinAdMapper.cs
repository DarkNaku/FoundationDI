using System;

namespace DarkNaku.FoundationDI
{
    // MaxSdkBase.AdInfo/ErrorInfo -> AdImpression/AdError 변환만 담당한다. MAX SDK 타입을
    // 아는 유일한 지점을 여기 하나로 모아, 어댑터 쪽 콜백 핸들러는 매핑 규칙을 반복하지 않는다.
    //
    // MapRevenuePrecision(string)만은 MAX 타입을 전혀 참조하지 않는 순수 함수다 — 원래는
    // EditMode 테스트로 핀 하고 싶었지만, FoundationDI.Tests가 이 값을 검증하려면 이 어셈블리
    // (FoundationDI.AppLovin, FOUNDATIONDI_APPLOVIN 심볼 게이트)를 참조해야 하고, 그러면
    // 심볼이 꺼진 상태(패키지 기본값)에서 FoundationDI.Tests 자체가 이 참조를 어떻게 다루는지
    // 검증된 바 없다 — README/설계서가 보장하는 것은 "그 asmdef 자신이 스킵된다"는 것뿐이고,
    // "그걸 참조하는 다른 asmdef가 안전하다"는 것은 별개의 명제다. 테스트 어셈블리 전체를
    // 쪼개는 설계 변경 없이는 이 함수 하나 때문에 검증되지 않은 조합을 프로덕션 baseline에
    // 끌어들이는 셈이라 판단해, 핀 테스트를 추가하지 않고 스위치 4갈래로만 남겨 둔다.
    internal static class AppLovinAdMapper
    {
        private const string Platform = "AppLovin";

        // MAX의 ILRD(임프레션 수익 데이터)는 항상 USD다. AdInfo에는 통화 필드 자체가 없다 —
        // AdMob처럼 퍼블리셔 통화가 섞일 수 있는 SDK를 위해 AdImpression이 Currency를
        // 들고 있을 뿐, MAX 어댑터 입장에서는 상수다.
        private const string Currency = "USD";

        public static AdImpression ToAdImpression(this MaxSdkBase.AdInfo info, AdFormat format)
        {
            return new AdImpression(
                format,
                Platform,
                info.NetworkName,
                info.AdUnitIdentifier,
                info.NetworkPlacement,
                null, // Placement는 정책 계층(FullScreenAdUnit/BannerAdUnit)이 스탬프한다.
                info.Revenue,
                Currency,
                MapRevenuePrecision(info.RevenuePrecision),
                info.CreativeIdentifier);
        }

        public static AdError ToAdError(this MaxSdkBase.ErrorInfo error)
        {
            return new AdError((int)error.Code, error.Message);
        }

        // MAX 네이티브 SDK가 내려주는 문자열 그대로를 매핑한다(대소문자 방어를 위해 순서
        // 무관 비교). 알려지지 않은 값(향후 SDK 버전에서 새 정밀도가 추가되는 경우 포함)은
        // 크래시 대신 Unknown으로 떨어뜨린다.
        internal static AdRevenuePrecision MapRevenuePrecision(string precision)
        {
            if (string.Equals(precision, "publisher_defined", StringComparison.OrdinalIgnoreCase))
                return AdRevenuePrecision.PublisherDefined;

            if (string.Equals(precision, "exact", StringComparison.OrdinalIgnoreCase))
                return AdRevenuePrecision.Exact;

            if (string.Equals(precision, "estimated", StringComparison.OrdinalIgnoreCase))
                return AdRevenuePrecision.Estimated;

            return AdRevenuePrecision.Unknown;
        }
    }
}
