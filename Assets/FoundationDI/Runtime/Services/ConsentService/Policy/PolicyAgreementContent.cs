namespace DarkNaku.FoundationDI
{
    // 약관 수락 창에 표시할 문구. 설정 에셋이 기본값을 만들고, 현지화가 필요하면 게임이
    // IConsentService.SetPolicyContent로 런타임에 바꾼다.
    public readonly struct PolicyAgreementContent
    {
        public string Title { get; }
        public string Message { get; }
        public string AcceptLabel { get; }
        public string TermsLabel { get; }
        public string TermsUrl { get; }
        public string PrivacyLabel { get; }
        public string PrivacyUrl { get; }

        public PolicyAgreementContent(string title, string message, string acceptLabel,
                                      string termsLabel, string termsUrl,
                                      string privacyLabel, string privacyUrl)
        {
            Title = title;
            Message = message;
            AcceptLabel = acceptLabel;
            TermsLabel = termsLabel;
            TermsUrl = termsUrl;
            PrivacyLabel = privacyLabel;
            PrivacyUrl = privacyUrl;
        }
    }
}
