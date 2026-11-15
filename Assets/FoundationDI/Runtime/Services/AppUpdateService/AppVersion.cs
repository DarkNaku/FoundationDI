using System;
using System.Globalization;

namespace DarkNaku.FoundationDI
{
    // 점으로 나뉜 버전 문자열(1.10.0)을 마디마다 숫자로 비교한다. 문자열 비교면 "1.10.0" < "1.9.3"이다.
    public static class AppVersion
    {
        // left가 크면 양수, 같으면 0, 작으면 음수. 자릿수가 다르면 빈 자리를 0으로 본다(1.2 = 1.2.0).
        // 숫자가 아닌 마디(1.2.0-beta)가 있으면 null — "같다"로 돌려주면 업데이트 없음으로 오해한다.
        public static int? Compare(string left, string right)
        {
            if (!TryParse(left, out var leftParts) || !TryParse(right, out var rightParts)) return null;

            var length = Math.Max(leftParts.Length, rightParts.Length);

            for (var i = 0; i < length; i++)
            {
                var leftNumber = i < leftParts.Length ? leftParts[i] : 0;
                var rightNumber = i < rightParts.Length ? rightParts[i] : 0;

                if (leftNumber != rightNumber) return leftNumber.CompareTo(rightNumber);
            }

            return 0;
        }

        private static bool TryParse(string version, out int[] parts)
        {
            parts = null;

            if (string.IsNullOrWhiteSpace(version)) return false;

            var texts = version.Trim().Split('.');
            var numbers = new int[texts.Length];

            for (var i = 0; i < texts.Length; i++)
            {
                if (!int.TryParse(texts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
            }

            parts = numbers;
            return true;
        }
    }
}
