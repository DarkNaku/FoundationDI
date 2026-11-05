using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 네이티브 약관 창의 본문. 링크 위치는 UTF-16 단위다 — C# string, NSString, Java String이 같은 단위라
    // 플랫폼마다 다시 계산하지 않고 그대로 넘긴다.
    public readonly struct PolicyLink
    {
        public int Start { get; }
        public int Length { get; }
        public string Url { get; }

        public PolicyLink(int start, int length, string url)
        {
            Start = start;
            Length = length;
            Url = url;
        }
    }

    public sealed class PolicyMessage
    {
        public string Text { get; }
        public IReadOnlyList<PolicyLink> Links { get; }

        public PolicyMessage(string text, IReadOnlyList<PolicyLink> links)
        {
            Text = text;
            Links = links;
        }
    }
}
