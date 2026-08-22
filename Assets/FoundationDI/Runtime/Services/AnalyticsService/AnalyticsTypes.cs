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

    // 구매 한 건. 5사가 전부 다른 예약 이벤트 이름과 매출 전달 방식을 갖고 있어서
    // (Firebase는 purchase + value 파라미터, Adjust는 대시보드 토큰 + setRevenue 전용 API 등)
    // 게임 코드가 SDK 중립적으로 넘길 수 있는 한 가지 모양이 필요하다. 번역은 어댑터가 한다.
    public readonly struct PurchaseInfo
    {
        public string ProductId { get; }
        public double Price { get; }          // 단가
        public string Currency { get; }       // ISO 4217 ("USD", "KRW")
        public int Quantity { get; }
        public string TransactionId { get; }
        public AnalyticsParams Extra { get; } // 게임 고유 컨텍스트. null 허용

        public double Revenue => Price * Quantity;

        public PurchaseInfo(string productId, double price, string currency,
                            int quantity = 1, string transactionId = null, AnalyticsParams extra = null)
        {
            ProductId = productId;
            Price = price;
            Currency = currency;
            Quantity = quantity;
            TransactionId = transactionId;
            Extra = extra;
        }
    }

    // AnalyticsService가 ScriptableObject를 직접 참조하지 않게 하는 값 타입.
    // EditMode 테스트가 SO 없이 서비스를 조립할 수 있다.
    public readonly struct AnalyticsServiceOptions
    {
        public bool CollectionEnabledByDefault { get; }

        public AnalyticsServiceOptions(bool collectionEnabledByDefault)
        {
            CollectionEnabledByDefault = collectionEnabledByDefault;
        }
    }
}
