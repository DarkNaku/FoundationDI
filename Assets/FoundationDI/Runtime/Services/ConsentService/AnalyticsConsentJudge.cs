namespace DarkNaku.FoundationDI
{
    // UMP 결과를 "분석 수집을 켜도 되는가"로 번역한다.
    //
    // GDPR이 적용되면 TCF Purpose 1(기기 저장·접근)을 본다. Firebase의 analytics_storage는 UMP
    // consent mode가 따로 기록하지만 그 값을 읽는 공개 경로가 없어 Purpose 1로 근사한다 —
    // 실기에서 대조해 확정할 항목이다(스펙 위험 R3). 판정 규칙이 바뀌면 이 함수만 고친다.
    internal static class AnalyticsConsentJudge
    {
        public static AnalyticsConsent Judge(UmpConsentStatus status, TcfSignals tcf)
        {
            if (status == UmpConsentStatus.NotRequired) return AnalyticsConsent.Granted;

            if (tcf.GdprApplies == true)
            {
                if (string.IsNullOrEmpty(tcf.PurposeConsents)) return AnalyticsConsent.Unknown;

                return tcf.HasPurpose(1) ? AnalyticsConsent.Granted : AnalyticsConsent.Denied;
            }

            if (tcf.GdprApplies == false) return AnalyticsConsent.Granted;

            return status == UmpConsentStatus.Obtained ? AnalyticsConsent.Granted : AnalyticsConsent.Unknown;
        }
    }
}
