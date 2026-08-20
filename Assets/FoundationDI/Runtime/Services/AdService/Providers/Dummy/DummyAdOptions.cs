using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public struct DummyAdOptions
    {
        [Tooltip("가짜 광고 로드에 걸리는 시간(초). 실제 SDK의 로드 지연을 흉내낸다.")]
        [SerializeField] private float _loadDelaySeconds;

        [Tooltip("로드 실패 확률(0~1). 재시도·백오프를 실기에서 검증하려면 0.3 정도로 올린다.")]
        [Range(0f, 1f)]
        [SerializeField] private float _failureRate;

        [Tooltip("가짜 전면/보상 광고가 화면에 떠 있는 시간(초).")]
        [SerializeField] private float _adDurationSeconds;

        [Tooltip("가짜 배너의 높이(화면 픽셀).")]
        [SerializeField] private float _bannerHeight;

        public DummyAdOptions(float loadDelaySeconds, float failureRate,
                              float adDurationSeconds, float bannerHeight)
        {
            _loadDelaySeconds = loadDelaySeconds;
            _failureRate = failureRate;
            _adDurationSeconds = adDurationSeconds;
            _bannerHeight = bannerHeight;
        }

        public float LoadDelaySeconds => _loadDelaySeconds;
        public float FailureRate => _failureRate;
        public float AdDurationSeconds => _adDurationSeconds;
        public float BannerHeight => _bannerHeight;

        public static DummyAdOptions Default => new(1f, 0f, 3f, 100f);
    }
}
