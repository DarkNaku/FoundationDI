using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "AdServiceSettings", menuName = "FoundationDI/Ad Service Settings")]
    public class AdServiceSettings : ScriptableObject
    {
        // 0 이하로 두면 지수 백오프가 무너진다 — base=0은 Mathf.Pow(0, n)이 항상 0이라
        // 즉시재시도 루프가 되고, maxDelay<=0은 Mathf.Min으로 모든 지연을 그 값으로
        // 눌러버린다(음수면 더 나쁘다). [Min] 어트리뷰트는 인스펙터만 지키므로
        // ToOptions()에서도 같은 값으로 한 번 더 클램프한다.
        private const float MinRetrySeconds = 0.1f;

        [Header("Provider")]
        [SerializeField] private AdProviderType _provider = AdProviderType.Dummy;

        [Tooltip("에디터에서는 항상 Dummy provider를 쓴다. 실기 테스트가 필요할 때만 끈다.")]
        [SerializeField] private bool _forceDummyInEditor = true;

        [Tooltip("LevelPlay의 appKey / AppLovin MAX의 sdkKey. AdMob은 비워둔다.")]
        [SerializeField] private AdUnitId _appKey;

        [Header("Ad Units")]
        [SerializeField] private AdUnitId _bannerUnitId;
        [SerializeField] private AdUnitId _interstitialUnitId;
        [SerializeField] private AdUnitId _rewardedUnitId;

        [Header("Banner")]
        [SerializeField] private BannerPosition _bannerPosition = BannerPosition.Bottom;
        [SerializeField] private BannerSize _bannerSize = BannerSize.Adaptive;
        [SerializeField] private bool _useAdaptiveBanner = true;

        [Header("Policy")]
        [Tooltip("초기화 직후 전면/보상 광고를 미리 로드한다.")]
        [SerializeField] private bool _autoLoadOnInitialize = true;

        [SerializeField, Min(0)] private int _maxRetryAttempts = 5;
        [SerializeField, Min(MinRetrySeconds)] private float _retryBaseSeconds = 2f;
        [SerializeField, Min(MinRetrySeconds)] private float _maxRetryDelaySeconds = 64f;

        [Tooltip("닫힘 이벤트 후 늦게 오는 보상 이벤트를 기다릴 프레임 수. " +
                 "0으로 두면 닫힘이 보상보다 먼저 오는 네트워크에서 보상을 잃는다.")]
        [SerializeField] private int _rewardGraceFrames = 1;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogging;
        [SerializeField] private bool _testMode;
        [SerializeField] private List<string> _testDeviceIds = new();

        [Header("Dummy Provider")]
        [SerializeField] private DummyAdOptions _dummyOptions = DummyAdOptions.Default;

        public AdProviderType Provider => _provider;
        public bool ForceDummyInEditor => _forceDummyInEditor;
        public DummyAdOptions DummyOptions => _dummyOptions;

        public AdServiceOptions ToOptions()
        {
            // [Min]은 인스펙터 편집에만 적용된다 — 스크립트로 생성했거나(EditMode 테스트),
            // 손으로 고친 .asset의 값은 그대로 들어온다. 여기서 다시 한번 클램프한다.
            var maxRetryAttempts = Mathf.Max(0, _maxRetryAttempts);
            var retryBaseSeconds = Mathf.Max(MinRetrySeconds, _retryBaseSeconds);
            var maxRetryDelaySeconds = Mathf.Max(MinRetrySeconds, _maxRetryDelaySeconds);

            return new AdServiceOptions(
                banner: _bannerUnitId,
                interstitial: _interstitialUnitId,
                rewarded: _rewardedUnitId,
                bannerOptions: new BannerOptions(_bannerPosition, _bannerSize, _useAdaptiveBanner),
                providerContext: new AdProviderContext(_appKey.Current, _verboseLogging, _testMode, _testDeviceIds),
                retryPolicy: new AdRetryPolicy(maxRetryAttempts, retryBaseSeconds, maxRetryDelaySeconds),
                rewardGraceFrames: _rewardGraceFrames,
                autoLoadOnInitialize: _autoLoadOnInitialize);
        }
    }
}
