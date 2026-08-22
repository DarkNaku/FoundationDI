using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firebase.Analytics;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AnalyticsParams를 Firebase.Analytics.Parameter로 옮기고, Firebase 고유의 이름 제약을 검사한다.
    //
    // 이 검사를 정책 계층(AnalyticsService)에 두지 않은 이유는 제약이 Firebase 고유이기 때문이다 —
    // AppsFlyer는 af_ 접두어를 오히려 요구하고, Adjust는 이름 대신 토큰을 쓴다. 공통 계층이
    // 가장 빡빡한 규칙을 강요하면 다른 SDK에서 멀쩡한 이벤트가 막힌다.
    //
    // 어긋나도 버리지 않고 경고만 남긴다. 판단은 SDK에게 맡기고 개발자에게만 알린다 —
    // Firebase는 규칙에 어긋난 이벤트를 런타임에 조용히 버리기 때문에, 경고가 없으면
    // "왜 대시보드에 안 보이지"를 며칠 뒤에야 알게 된다.
    internal static class FirebaseParamConverter
    {
        private const int MaxNameLength = 40;
        private const int MaxParameterCount = 25;

        private static readonly Parameter[] _empty = new Parameter[0];

        private static readonly Regex _validName = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private static readonly string[] _reservedPrefixes = { "firebase_", "google_", "ga_" };

        public static Parameter[] Convert(AnalyticsParams parameters)
        {
            if (parameters == null || parameters.Count == 0) return _empty;

            var target = new List<Parameter>(parameters.Count);
            Append(target, parameters);
            return target.ToArray();
        }

        public static void Append(List<Parameter> target, AnalyticsParams parameters)
        {
            if (parameters == null) return;

            foreach (var pair in parameters)
            {
                WarnIfInvalidName(pair.Key, "파라미터 이름");
                target.Add(Create(pair.Key, pair.Value));
            }
        }

        public static Parameter Create(string key, AnalyticsParamValue value)
        {
            switch (value.Kind)
            {
                case AnalyticsParamKind.Long: return new Parameter(key, value.LongValue);
                case AnalyticsParamKind.Double: return new Parameter(key, value.DoubleValue);
                default: return new Parameter(key, value.StringValue ?? string.Empty);
            }
        }

        public static void WarnIfInvalidEventName(string name)
        {
            WarnIfInvalidName(name, "이벤트 이름");
        }

        public static void WarnIfTooManyParameters(string eventName, int count)
        {
            if (count <= MaxParameterCount) return;

            Debug.LogWarning($"[Analytics/Firebase] '{eventName}' 의 파라미터가 {count}개다. " +
                             $"Firebase 상한은 {MaxParameterCount}개이며 초과분은 조용히 버려진다.");
        }

        private static void WarnIfInvalidName(string name, string what)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"[Analytics/Firebase] {what}이 비어 있다.");
                return;
            }

            if (name.Length > MaxNameLength)
            {
                Debug.LogWarning($"[Analytics/Firebase] {what} '{name}' 이 {MaxNameLength}자를 넘는다.");
            }

            if (!_validName.IsMatch(name))
            {
                Debug.LogWarning($"[Analytics/Firebase] {what} '{name}' 이 규칙에 어긋난다. " +
                                 "영문자로 시작하고 영숫자와 밑줄만 쓸 수 있다.");
            }

            foreach (var prefix in _reservedPrefixes)
            {
                if (!name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;

                Debug.LogWarning($"[Analytics/Firebase] {what} '{name}' 이 예약 접두어 '{prefix}' 로 시작한다.");
                break;
            }
        }
    }
}
