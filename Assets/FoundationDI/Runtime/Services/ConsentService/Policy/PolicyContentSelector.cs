using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 기기 언어(Application.systemLanguage)에 맞는 약관 문구를 고른다. 없으면 설정의 기본 문구다.
    internal static class PolicyContentSelector
    {
        public static PolicyAgreementContent Select(PolicyAgreementContent fallback,
                                                    IReadOnlyList<LocalizedPolicyText> entries,
                                                    SystemLanguage language)
        {
            var entry = Find(entries, language) ?? FindChineseSibling(entries, language);

            return entry == null ? fallback : entry.Over(fallback);
        }

        private static LocalizedPolicyText Find(IReadOnlyList<LocalizedPolicyText> entries, SystemLanguage language)
        {
            if (entries == null) return null;

            foreach (var entry in entries)
            {
                if (entry != null && entry.Language == language) return entry;
            }

            return null;
        }

        // 기기는 간체/번체로 보고하는데 설정에는 통합 Chinese 한 줄만 둘 수 있다(반대도 마찬가지).
        // 간체와 번체끼리는 대신하지 않는다 — 다른 글자 체계다.
        private static LocalizedPolicyText FindChineseSibling(IReadOnlyList<LocalizedPolicyText> entries,
                                                              SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                    return Find(entries, SystemLanguage.Chinese);
                case SystemLanguage.Chinese:
                    return Find(entries, SystemLanguage.ChineseSimplified) ??
                           Find(entries, SystemLanguage.ChineseTraditional);
                default:
                    return null;
            }
        }
    }
}
