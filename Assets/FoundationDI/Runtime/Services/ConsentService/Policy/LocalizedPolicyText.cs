using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // ConsentServiceSettings의 언어별 약관 문구 한 줄. 빈 칸은 설정의 기본 문구로 채운다
    // (언어마다 URL이 같으면 URL은 비워 두면 된다).
    [Serializable]
    public sealed class LocalizedPolicyText
    {
        [SerializeField] private SystemLanguage _language = SystemLanguage.Korean;
        [SerializeField] private string _title = "";
        [SerializeField, TextArea(3, 8)] private string _message = "";
        [SerializeField] private string _acceptLabel = "";
        [SerializeField] private string _termsLabel = "";
        [SerializeField] private string _termsUrl = "";
        [SerializeField] private string _privacyLabel = "";
        [SerializeField] private string _privacyUrl = "";

        public LocalizedPolicyText()
        {
        }

        public LocalizedPolicyText(SystemLanguage language, string title, string message, string acceptLabel,
                                   string termsLabel, string termsUrl, string privacyLabel, string privacyUrl)
        {
            _language = language;
            _title = title;
            _message = message;
            _acceptLabel = acceptLabel;
            _termsLabel = termsLabel;
            _termsUrl = termsUrl;
            _privacyLabel = privacyLabel;
            _privacyUrl = privacyUrl;
        }

        public SystemLanguage Language => _language;

        // 빈 칸은 fallback의 같은 칸으로 채운다.
        public PolicyAgreementContent Over(PolicyAgreementContent fallback) =>
            new(Or(_title, fallback.Title), Or(_message, fallback.Message), Or(_acceptLabel, fallback.AcceptLabel),
                Or(_termsLabel, fallback.TermsLabel), Or(_termsUrl, fallback.TermsUrl),
                Or(_privacyLabel, fallback.PrivacyLabel), Or(_privacyUrl, fallback.PrivacyUrl));

        private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
    }
}
