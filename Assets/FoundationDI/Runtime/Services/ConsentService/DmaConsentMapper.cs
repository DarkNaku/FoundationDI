namespace DarkNaku.FoundationDI
{
    public readonly struct DmaConsent
    {
        public bool? Eea { get; }
        public bool AdUserData { get; }
        public bool AdPersonalization { get; }

        public DmaConsent(bool? eea, bool adUserData, bool adPersonalization)
        {
            Eea = eea;
            AdUserData = adUserData;
            AdPersonalization = adPersonalization;
        }
    }

    // TCF 동의를 Google DMA 신호(eea / ad_user_data / ad_personalization)로 번역한다.
    // Adjust처럼 TCF를 스스로 읽지 않는 MMP에 넘길 때 쓴다.
    //
    // 매핑은 Google의 TCF → consent mode 매핑을 따른 이 패키지의 해석이다(P1·P7 → ad_user_data,
    // P3·P4 → ad_personalization). Adjust가 정한 규칙은 아니다.
    public static class DmaConsentMapper
    {
        // GDPR 판정이 없으면(UMP 미사용) false. eea를 빼고 보내면 Google이 EEA로 간주하므로,
        // 모르는 상태에서 허용값을 보내는 것보다 아무것도 안 보내는 쪽이 안전하다.
        public static bool TryMap(TcfSignals tcf, out DmaConsent consent)
        {
            if (!tcf.GdprApplies.HasValue)
            {
                consent = default;
                return false;
            }

            if (tcf.GdprApplies == false)
            {
                consent = new DmaConsent(false, true, true);
                return true;
            }

            consent = new DmaConsent(true,
                                     tcf.HasPurpose(1) && tcf.HasPurpose(7),
                                     tcf.HasPurpose(3) && tcf.HasPurpose(4));
            return true;
        }
    }
}
