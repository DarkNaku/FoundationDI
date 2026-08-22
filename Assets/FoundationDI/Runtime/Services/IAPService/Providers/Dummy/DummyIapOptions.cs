using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public struct DummyIapOptions
    {
        [Tooltip("가짜 스토어 시트가 떠 있는 시간(초). 0이면 즉시 결과가 나온다.")]
        [SerializeField, Min(0f)] private float _delaySeconds;

        [Tooltip("모든 구매를 실패시킨다. 실패 UI를 확인할 때 켠다.")]
        [SerializeField] private bool _alwaysFail;

        [Tooltip("모든 구매를 사용자 취소로 끝낸다. 취소 경로를 확인할 때 켠다.")]
        [SerializeField] private bool _alwaysCancel;

        [Tooltip("가짜 상품에 표시할 가격 문자열.")]
        [SerializeField] private string _priceFormat;

        public DummyIapOptions(float delaySeconds, bool alwaysFail, bool alwaysCancel, string priceFormat)
        {
            _delaySeconds = delaySeconds;
            _alwaysFail = alwaysFail;
            _alwaysCancel = alwaysCancel;
            _priceFormat = priceFormat;
        }

        public float DelaySeconds => _delaySeconds;
        public bool AlwaysFail => _alwaysFail;
        public bool AlwaysCancel => _alwaysCancel;
        public string PriceFormat => string.IsNullOrEmpty(_priceFormat) ? "$0.99" : _priceFormat;

        public static DummyIapOptions Default => new(0.5f, false, false, "$0.99");
    }
}
