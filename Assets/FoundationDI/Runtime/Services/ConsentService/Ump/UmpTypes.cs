using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 네이티브 enum을 그대로 캐스팅하지 않는다 — Android와 iOS의 정수 값 순서가 다르다
    // (UmpStatusMapper가 플랫폼별로 명시적으로 번역한다).
    public enum UmpConsentStatus
    {
        Unknown,
        NotRequired,
        Required,
        Obtained,
    }

    public enum UmpDebugGeography
    {
        Disabled,
        Eea,
        NotEea,
        RegulatedUsState,
        Other,
    }

    public readonly struct UmpRequestOptions
    {
        // 개발 빌드에서만 의미가 있다. 설정이 릴리스 빌드에서는 Disabled로 만든다.
        public UmpDebugGeography DebugGeography { get; }
        public IReadOnlyList<string> TestDeviceHashedIds { get; }
        public bool TagForUnderAgeOfConsent { get; }

        public UmpRequestOptions(UmpDebugGeography debugGeography, IReadOnlyList<string> testDeviceHashedIds,
                                 bool tagForUnderAgeOfConsent)
        {
            DebugGeography = debugGeography;
            TestDeviceHashedIds = testDeviceHashedIds;
            TagForUnderAgeOfConsent = tagForUnderAgeOfConsent;
        }
    }
}
