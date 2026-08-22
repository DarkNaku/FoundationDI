using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum AnalyticsParamKind
    {
        String,
        Long,
        Double,
    }

    // 분석 파라미터 하나의 값. string/long/double 3-way union이다.
    // 이 세 가지인 이유는 Firebase.Analytics.Parameter의 생성자가 정확히 이 셋을 받기 때문이고,
    // 나머지 SDK도 이보다 넓은 타입을 요구하지 않는다. union으로 둔 덕에 박싱이 없다.
    public readonly struct AnalyticsParamValue
    {
        public AnalyticsParamKind Kind { get; }
        public string StringValue { get; }
        public long LongValue { get; }
        public double DoubleValue { get; }

        private AnalyticsParamValue(AnalyticsParamKind kind, string s, long l, double d)
        {
            Kind = kind;
            StringValue = s;
            LongValue = l;
            DoubleValue = d;
        }

        public static AnalyticsParamValue Of(string value) =>
            new(AnalyticsParamKind.String, value, 0L, 0d);

        public static AnalyticsParamValue Of(long value) =>
            new(AnalyticsParamKind.Long, null, value, 0d);

        public static AnalyticsParamValue Of(double value) =>
            new(AnalyticsParamKind.Double, null, 0L, value);

        public override string ToString() => Kind switch
        {
            AnalyticsParamKind.Long => LongValue.ToString(),
            AnalyticsParamKind.Double => DoubleValue.ToString("R"),
            _ => StringValue,
        };
    }

    // 자유형 이벤트의 파라미터 묶음.
    //
    //   new AnalyticsParams { { "level", 12L }, { "clear_time", 34.5 } }
    //
    // Add 오버로드가 string/long/double 셋뿐이라 bool·enum·DateTime을 넣으면 컴파일 에러가 된다.
    // 의도된 마찰이다 — Firebase는 지원하지 않는 타입의 파라미터를 런타임에 조용히 버린다.
    //
    // struct가 아니라 class인 이유: 컬렉션 초기화 구문은 대상을 변형해야 성립하는데,
    // struct로 만들면 mutable struct라는 더 나쁜 함정이 된다.
    public sealed class AnalyticsParams : IEnumerable<KeyValuePair<string, AnalyticsParamValue>>
    {
        private readonly List<KeyValuePair<string, AnalyticsParamValue>> _items = new();

        public int Count => _items.Count;

        public void Add(string key, string value) => Add(key, AnalyticsParamValue.Of(value));

        public void Add(string key, long value) => Add(key, AnalyticsParamValue.Of(value));

        public void Add(string key, double value) => Add(key, AnalyticsParamValue.Of(value));

        private void Add(string key, AnalyticsParamValue value)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[AnalyticsService] 키가 비어 있는 파라미터는 무시한다.");
                return;
            }

            _items.Add(new KeyValuePair<string, AnalyticsParamValue>(key, value));
        }

        public List<KeyValuePair<string, AnalyticsParamValue>>.Enumerator GetEnumerator() =>
            _items.GetEnumerator();

        IEnumerator<KeyValuePair<string, AnalyticsParamValue>>
            IEnumerable<KeyValuePair<string, AnalyticsParamValue>>.GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
