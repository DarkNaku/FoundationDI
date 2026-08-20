using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AdServiceSettings(ScriptableObject)가 아니라 이 구조체를 받는다.
    // 서비스가 에셋 형식에 묶이지 않고, EditMode 테스트가 SO를 만들 필요도 없다.
    public readonly struct AdServiceOptions
    {
        public AdUnitId Banner { get; }
        public AdUnitId Interstitial { get; }
        public AdUnitId Rewarded { get; }
        public BannerOptions BannerOptions { get; }
        public AdProviderContext ProviderContext { get; }
        public AdRetryPolicy RetryPolicy { get; }
        public int RewardGraceFrames { get; }
        public bool AutoLoadOnInitialize { get; }

        public AdServiceOptions(AdUnitId banner, AdUnitId interstitial, AdUnitId rewarded,
                                BannerOptions bannerOptions, AdProviderContext providerContext,
                                AdRetryPolicy retryPolicy, int rewardGraceFrames, bool autoLoadOnInitialize)
        {
            Banner = banner;
            Interstitial = interstitial;
            Rewarded = rewarded;
            BannerOptions = bannerOptions;
            ProviderContext = providerContext;
            RetryPolicy = retryPolicy;
            RewardGraceFrames = rewardGraceFrames;
            AutoLoadOnInitialize = autoLoadOnInitialize;
        }
    }

    public class AdService : IAdService
    {
        private readonly IAdProvider _provider;
        private readonly IAdDispatcher _dispatcher;
        private readonly AdServiceOptions _options;
        private readonly IAdRemovalStorage _removalStorage;

        private FullScreenAdUnit _interstitial;
        private FullScreenAdUnit _rewarded;
        private BannerAdUnit _banner;

        private bool _adsRemoved;
        private bool _isDisposed;

        public event Action<AdFormat> Loaded;
        public event Action<AdFormat> Displayed;
        public event Action<AdFormat> Closed;
        public event Action<AdImpression> Paid;
        public event Action<bool> AdsRemovedChanged;

        public AdService(IAdProvider provider, IAdDispatcher dispatcher,
                         AdServiceOptions options, IAdRemovalStorage removalStorage)
        {
            _provider = provider;
            _dispatcher = dispatcher;
            _options = options;
            _removalStorage = removalStorage;
            _adsRemoved = removalStorage?.Load() ?? false;
        }

        public bool IsInitialized { get; private set; }

        public IAdConsent Consent => _provider.Consent;

        // InitializeAsync가 성공하기 전에는 null이다. provider가 초기화되기 전에는
        // 어댑터를 만들 수 없기 때문이다. 게임 코드는 초기화를 먼저 await 해야 한다.
        public IInterstitialAd Interstitial => _interstitial;
        public IRewardedAd Rewarded => _rewarded;
        public IBannerAd Banner => _banner;

        public async Awaitable<bool> InitializeAsync()
        {
            if (IsInitialized) return true;

            var ok = await _provider.InitializeAsync(_options.ProviderContext);
            if (!ok)
            {
                Debug.LogError($"[AdService] {_provider.Name} 초기화에 실패했다. 광고를 요청하지 않는다.");
                return false;
            }

            BuildAdUnits();
            IsInitialized = true;

            if (_options.AutoLoadOnInitialize)
            {
                _interstitial.Load();
                _rewarded.Load();
            }

            return true;
        }

        private void BuildAdUnits()
        {
            _interstitial = new FullScreenAdUnit(
                _provider.CreateInterstitial(_options.Interstitial.Current), _dispatcher,
                AdFormat.Interstitial, _options.RetryPolicy, _options.RewardGraceFrames, () => _adsRemoved);

            _rewarded = new FullScreenAdUnit(
                _provider.CreateRewarded(_options.Rewarded.Current), _dispatcher,
                AdFormat.Rewarded, _options.RetryPolicy, _options.RewardGraceFrames, () => _adsRemoved);

            _banner = new BannerAdUnit(
                () => _provider.CreateBanner(_options.Banner.Current, _options.BannerOptions),
                () => _adsRemoved);

            Wire(_interstitial, AdFormat.Interstitial);
            Wire(_rewarded, AdFormat.Rewarded);

            _banner.Paid += OnPaid;

            // 어댑터별 Paid와 provider 전역 ImpressionPaid를 하나의 공개 이벤트로 합류시킨다.
            _provider.ImpressionPaid += OnPaid;
        }

        private void Wire(FullScreenAdUnit unit, AdFormat format)
        {
            unit.Loaded += () => Loaded?.Invoke(format);
            unit.Displayed += () => Displayed?.Invoke(format);
            unit.Closed += () => Closed?.Invoke(format);
            unit.Paid += OnPaid;
        }

        private void OnPaid(AdImpression impression) => Paid?.Invoke(impression);

        public bool AdsRemoved
        {
            get => _adsRemoved;
            set
            {
                if (_adsRemoved == value) return;

                _adsRemoved = value;
                _removalStorage?.Save(value);
                _banner?.OnAdsRemovedChanged(value);
                AdsRemovedChanged?.Invoke(value);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_provider != null) _provider.ImpressionPaid -= OnPaid;

            _interstitial?.Dispose();
            _rewarded?.Dispose();
            _banner?.Dispose();
            _provider?.Dispose();

            IsInitialized = false;
        }
    }
}
