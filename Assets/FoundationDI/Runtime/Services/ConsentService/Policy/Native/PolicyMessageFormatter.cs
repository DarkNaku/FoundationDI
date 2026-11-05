using System;
using System.Collections.Generic;
using System.Text;

namespace DarkNaku.FoundationDI
{
    // 약관 문구를 "본문 + 링크 위치"로 바꾼다. 링크를 거는 자리는 이 순서로 정한다.
    //   1. 본문의 {terms} / {privacy} 자리표시자 — 라벨로 바꾸고 그 자리에 링크
    //   2. 자리표시자가 없으면 본문에 있는 라벨의 첫 위치 — "당사의 이용약관과 …"처럼 문장 속 단어에 링크
    //   3. 본문에 라벨도 없으면 끝에 줄을 바꿔 붙인다 — 링크가 아예 없으면 약관을 열 방법이 없다
    // URL이 비어 있으면 라벨만 넣고 링크는 걸지 않는다(DefaultPolicyAgreementPrompt가 버튼을 숨기는 것과 같다).
    internal static class PolicyMessageFormatter
    {
        internal const string TermsToken = "{terms}";
        internal const string PrivacyToken = "{privacy}";

        public static PolicyMessage Format(PolicyAgreementContent content)
        {
            var source = content.Message ?? string.Empty;
            var termsLabel = content.TermsLabel ?? string.Empty;
            var privacyLabel = content.PrivacyLabel ?? string.Empty;

            var text = new StringBuilder(source.Length);
            var links = new List<PolicyLink>();
            var termsPlaced = false;
            var privacyPlaced = false;

            for (var i = 0; i < source.Length;)
            {
                if (string.CompareOrdinal(source, i, TermsToken, 0, TermsToken.Length) == 0)
                {
                    Place(text, links, termsLabel, content.TermsUrl);
                    termsPlaced = true;
                    i += TermsToken.Length;
                }
                else if (string.CompareOrdinal(source, i, PrivacyToken, 0, PrivacyToken.Length) == 0)
                {
                    Place(text, links, privacyLabel, content.PrivacyUrl);
                    privacyPlaced = true;
                    i += PrivacyToken.Length;
                }
                else
                {
                    text.Append(source[i]);
                    i++;
                }
            }

            var appended = false;

            if (!termsPlaced) LinkOrAppend(text, links, termsLabel, content.TermsUrl, ref appended);
            if (!privacyPlaced) LinkOrAppend(text, links, privacyLabel, content.PrivacyUrl, ref appended);

            links.Sort((a, b) => a.Start.CompareTo(b.Start));
            return new PolicyMessage(text.ToString(), links);
        }

        private static void Place(StringBuilder text, List<PolicyLink> links, string label, string url)
        {
            if (!string.IsNullOrEmpty(url) && label.Length > 0) links.Add(new PolicyLink(text.Length, label.Length, url));

            text.Append(label);
        }

        private static void LinkOrAppend(StringBuilder text, List<PolicyLink> links, string label, string url,
                                         ref bool appended)
        {
            if (string.IsNullOrEmpty(url) || label.Length == 0) return;

            var current = text.ToString();

            for (var from = 0; ;)
            {
                var index = current.IndexOf(label, from, StringComparison.Ordinal);

                if (index < 0) break;

                if (!Overlaps(links, index, label.Length))
                {
                    links.Add(new PolicyLink(index, label.Length, url));
                    return;
                }

                from = index + 1;
            }

            // 처음 붙일 때만 문단을 띄운다. 두 번째 라벨은 바로 아랫줄이다.
            text.Append(appended ? "\n" : current.Length > 0 ? "\n\n" : string.Empty);
            appended = true;
            Place(text, links, label, url);
        }

        private static bool Overlaps(List<PolicyLink> links, int start, int length)
        {
            foreach (var link in links)
            {
                if (start < link.Start + link.Length && link.Start < start + length) return true;
            }

            return false;
        }
    }
}
