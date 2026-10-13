using System;

namespace DarkNaku.FoundationDI
{
    // 분석 수집을 켜도 되는가. Unknown은 "아직 판정하지 못했다"(첫 실행 오프라인 등)라
    // 수집을 켜지도 버퍼를 버리지도 않는다.
    public enum AnalyticsConsent
    {
        Unknown,
        Granted,
        Denied,
    }

    // UMP가 IAB TCF 저장소(Android 기본 SharedPreferences / iOS NSUserDefaults)에 쓴 값 중
    // 이 패키지가 읽는 것만 담는다. Unity PlayerPrefs와는 다른 저장소다.
    public readonly struct TcfSignals : IEquatable<TcfSignals>
    {
        // IABTCF_gdprApplies. 1이면 true, 0이면 false, 키가 없으면 null.
        public bool? GdprApplies { get; }

        // IABTCF_PurposeConsents. "1010..." 형태이며 0번 문자가 Purpose 1이다.
        public string PurposeConsents { get; }

        public TcfSignals(bool? gdprApplies, string purposeConsents)
        {
            GdprApplies = gdprApplies;
            PurposeConsents = purposeConsents;
        }

        public static TcfSignals None => new(null, null);

        public bool HasPurpose(int purpose)
        {
            if (string.IsNullOrEmpty(PurposeConsents)) return false;
            if (purpose < 1 || purpose > PurposeConsents.Length) return false;

            return PurposeConsents[purpose - 1] == '1';
        }

        public bool Equals(TcfSignals other) =>
            GdprApplies == other.GdprApplies && PurposeConsents == other.PurposeConsents;

        public override bool Equals(object obj) => obj is TcfSignals other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(GdprApplies, PurposeConsents);
    }

    public readonly struct ConsentResult : IEquatable<ConsentResult>
    {
        // 약관·개인정보처리방침을 수락했는가. false면 광고·분석 모두 시작하지 않는다.
        public bool PolicyAccepted { get; }

        // UMP canRequestAds. UMP를 쓰지 않으면 true.
        public bool CanRequestAds { get; }

        public AnalyticsConsent AnalyticsConsent { get; }

        public TrackingAuthorizationStatus TrackingStatus { get; }

        public TcfSignals Tcf { get; }

        public ConsentResult(bool policyAccepted, bool canRequestAds, AnalyticsConsent analyticsConsent,
                             TrackingAuthorizationStatus trackingStatus, TcfSignals tcf)
        {
            PolicyAccepted = policyAccepted;
            CanRequestAds = canRequestAds;
            AnalyticsConsent = analyticsConsent;
            TrackingStatus = trackingStatus;
            Tcf = tcf;
        }

        public bool Equals(ConsentResult other) =>
            PolicyAccepted == other.PolicyAccepted &&
            CanRequestAds == other.CanRequestAds &&
            AnalyticsConsent == other.AnalyticsConsent &&
            TrackingStatus == other.TrackingStatus &&
            Tcf.Equals(other.Tcf);

        public override bool Equals(object obj) => obj is ConsentResult other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(PolicyAccepted, CanRequestAds, AnalyticsConsent, TrackingStatus, Tcf);

        public override string ToString() =>
            $"Policy={PolicyAccepted} Ads={CanRequestAds} Analytics={AnalyticsConsent} ATT={TrackingStatus}";
    }
}
