using System;
using System.Collections.Generic;
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
        public float InterstitialCooldownSeconds { get; }
        public bool AutoLoadOnInitialize { get; }

        // 초기화 앞에서 iOS ATT 다이얼로그를 띄울지. 끄면 게임이 원하는 시점에
        // IAdService.TrackingAuthorization.RequestAsync()로 직접 묻는다(사전 안내 화면을
        // 먼저 보여주고 싶은 경우). iOS 실기가 아니면 켜 두어도 아무 일도 하지 않는다.
        public bool RequestTrackingAuthorization { get; }

        public AdServiceOptions(AdUnitId banner, AdUnitId interstitial, AdUnitId rewarded,
                                BannerOptions bannerOptions, AdProviderContext providerContext,
                                AdRetryPolicy retryPolicy, int rewardGraceFrames,
                                float interstitialCooldownSeconds, bool autoLoadOnInitialize,
                                bool requestTrackingAuthorization = true)
        {
            Banner = banner;
            Interstitial = interstitial;
            Rewarded = rewarded;
            BannerOptions = bannerOptions;
            ProviderContext = providerContext;
            RetryPolicy = retryPolicy;
            RewardGraceFrames = rewardGraceFrames;
            InterstitialCooldownSeconds = interstitialCooldownSeconds;
            AutoLoadOnInitialize = autoLoadOnInitialize;
            RequestTrackingAuthorization = requestTrackingAuthorization;
        }
    }

    public class AdService : IAdService
    {
        private readonly IAdProvider _provider;
        private readonly IAdDispatcher _dispatcher;
        private readonly AdServiceOptions _options;
        private readonly IAdRemovalStorage _removalStorage;
        private readonly ITrackingAuthorization _tracking;

        private FullScreenAdUnit _interstitial;
        private FullScreenAdUnit _rewarded;
        private BannerAdUnit _banner;

        private bool _adsRemoved;
        private bool _isDisposed;

        // 재진입 가드: InitializeAsync는 완료 전(IsInitialized가 아직 참이 되기 전)에
        // 두 번째로 불릴 수 있다(부트스트랩 시퀀스 + UI 화면이 같은 프레임에 각각 기다리는 등).
        // 진행 중 호출은 새 초기화를 시작하지 않고 이 리스트에 편승해 같은 결과를 받는다.
        // Awaitable은 단일 await만 허용하므로 대기자마다 별도 completion source가 필요하다
        // (ResourceService의 LoadAsync 대기자 패턴과 동일).
        private bool _initializing;
        private readonly List<AwaitableCompletionSource<bool>> _initWaiters = new();

        public event Action<AdFormat> Loaded;
        public event Action<AdFormat> Displayed;
        public event Action<AdFormat> Closed;
        public event Action<AdImpression> Paid;
        public event Action<bool> AdsRemovedChanged;

        // tracking은 선택 인자다. 넘기지 않으면 "물어볼 수 없는 플랫폼"으로 취급해
        // 초기화 경로가 그대로 동작한다 — 기존 호출부와 테스트를 깨지 않기 위해서다.
        public AdService(IAdProvider provider, IAdDispatcher dispatcher,
                         AdServiceOptions options, IAdRemovalStorage removalStorage,
                         ITrackingAuthorization tracking = null)
        {
            _provider = provider;
            _dispatcher = dispatcher;
            _options = options;
            _removalStorage = removalStorage;
            _tracking = tracking ?? new UnsupportedTrackingAuthorization();
            _adsRemoved = removalStorage?.Load() ?? false;
        }

        public bool IsInitialized { get; private set; }

        public IAdConsent Consent => _provider.Consent;

        public ITrackingAuthorization TrackingAuthorization => _tracking;

        // InitializeAsync가 성공하기 전에는 null이다. provider가 초기화되기 전에는
        // 어댑터를 만들 수 없기 때문이다. 게임 코드는 초기화를 먼저 await 해야 한다.
        public IInterstitialAd Interstitial => _interstitial;
        public IRewardedAd Rewarded => _rewarded;
        public IBannerAd Banner => _banner;

        public async Awaitable<bool> InitializeAsync()
        {
            if (_isDisposed) return false;
            if (IsInitialized) return true;

            // 이미 진행 중인 초기화가 있으면 새로 시작하지 않고 그 결과에 편승한다.
            // 그러지 않으면 BuildAdUnits()가 두 번 실행돼 provider.ImpressionPaid 구독이
            // 중복되고(수익 이벤트 이중 집계), 먼저 만든 광고 유닛이 Dispose 없이 버려진다.
            if (_initializing)
            {
                var waiter = new AwaitableCompletionSource<bool>();
                _initWaiters.Add(waiter);
                return await waiter.Awaitable;
            }

            _initializing = true;
            var ok = false;

            try
            {
                // provider보다 먼저 묻는다. 광고 SDK는 초기화 시점의 ATT 상태를 보고
                // IDFA 사용 여부와 타게팅 가능 여부를 결정하므로, 뒤에 물으면 그 세션의
                // 첫 요청들이 동의를 받고도 비타게팅으로 나간다.
                await RequestTrackingAuthorizationAsync();

                // 다이얼로그가 떠 있는 동안 씬이 언로드돼 서비스가 정리됐을 수 있다.
                if (_isDisposed) return false;

                // RequestAsync의 반환값이 아니라 Status를 다시 읽는다. 요청을 건너뛴 경로
                // (RequestTrackingAuthorization = false, 이미 답한 상태, 물을 수 없는 플랫폼)
                // 에서도 현재 상태를 provider에 실어야 하기 때문이다 — 반환값을 쓰면 그 세
                // 경로가 전부 NotDetermined로 뭉개진다.
                var context = _options.ProviderContext.WithTrackingStatus(_tracking.Status);

                ok = await _provider.InitializeAsync(context);

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
            finally
            {
                // 실패 경로에서도 진행 플래그를 반드시 내려야 다음 재시도가 다시 시도될 수 있다.
                _initializing = false;
                CompleteWaiters(ok);
            }
        }

        private async Awaitable RequestTrackingAuthorizationAsync()
        {
            if (!_options.RequestTrackingAuthorization) return;
            if (!_tracking.IsSupported) return;

            // 이미 답을 받았으면 다시 묻지 않는다. iOS는 두 번째 요청에 다이얼로그를
            // 띄우지 않고 기존 상태를 그대로 돌려주므로 왕복만 낭비된다.
            if (_tracking.Status != TrackingAuthorizationStatus.NotDetermined) return;

            try
            {
                var status = await _tracking.RequestAsync();

                Debug.Log($"[AdService] ATT 추적 권한: {status}");
            }
            catch (Exception e)
            {
                // 권한 요청이 깨져도 광고는 초기화되어야 한다. 비타게팅 광고는 여전히
                // 나가고, 여기서 던지면 그마저 잃는다.
                Debug.LogWarning($"[AdService] ATT 권한 요청 중 예외가 났다. 초기화를 계속한다: {e}");
            }
        }

        private void CompleteWaiters(bool result)
        {
            if (_initWaiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<bool>>(_initWaiters);
            _initWaiters.Clear();

            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(result);
            }
        }

        private void BuildAdUnits()
        {
            ValidateUnitId(_options.Interstitial, AdFormat.Interstitial);
            ValidateUnitId(_options.Rewarded, AdFormat.Rewarded);
            ValidateUnitId(_options.Banner, AdFormat.Banner);

            _interstitial = new FullScreenAdUnit(
                _provider.CreateInterstitial(_options.Interstitial.Current), _dispatcher,
                AdFormat.Interstitial, _options.RetryPolicy, _options.RewardGraceFrames,
                _options.InterstitialCooldownSeconds, () => _adsRemoved);

            // 보상형은 유저가 자발적으로 보는 것이라 쿨다운 대상이 아니다 — 0을 넘겨 게이트를 무력화한다.
            _rewarded = new FullScreenAdUnit(
                _provider.CreateRewarded(_options.Rewarded.Current), _dispatcher,
                AdFormat.Rewarded, _options.RetryPolicy, _options.RewardGraceFrames,
                0f, () => _adsRemoved);

            _banner = new BannerAdUnit(
                () => _provider.CreateBanner(_options.Banner.Current, _options.BannerOptions),
                () => _adsRemoved);

            Wire(_interstitial, AdFormat.Interstitial);
            Wire(_rewarded, AdFormat.Rewarded);

            _banner.Paid += OnPaid;

            // 어댑터별 Paid와 provider 전역 ImpressionPaid를 하나의 공개 이벤트로 합류시킨다.
            _provider.ImpressionPaid += OnPaid;
        }

        // AdUnitId.IsValid는 Current(빌드 타깃에 따라 Android/iOS 중 하나)를 본다 — 에디터의
        // 활성 빌드 타깃이 모바일이 아니면(예: 이 리포지토리의 기본 Standalone 타깃) 항상
        // 빈 값을 돌려주므로, 여기서는 그 값을 쓰지 않는다. 대신 Android/iOS 둘 다 비어
        // 있는지만 본다 — "둘 중 하나도 설정한 적이 없다"는 명백한 설정 누락만 잡고,
        // "현재 타깃엔 없지만 다른 플랫폼엔 있다"는 정상 상태를 오탐하지 않기 위함이다.
        private void ValidateUnitId(AdUnitId unitId, AdFormat format)
        {
            // Dummy provider는 광고단위 ID가 없어도 정상 동작하도록 설계됐다(README/스펙
            // 참고). 여기서 걸러내지 않으면 실기 스모크 테스트마다 매번 에러 로그가 찍힌다.
            if (_provider.Name == "Dummy") return;

            if (!string.IsNullOrEmpty(unitId.Android) || !string.IsNullOrEmpty(unitId.iOS)) return;

            Debug.LogError($"[AdService] {format} 광고단위 ID가 설정되지 않았다. " +
                           $"{_provider.Name} provider에 빈 ID로 광고를 요청하면 로드가 실패한다.");
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
                if (_isDisposed) return;
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

            // 해제된 유닛을 계속 돌려주지 않도록 널로 만든다. InitializeAsync는 위의
            // _isDisposed 가드로 다시 불리지 않으므로 이 상태로 고정된다.
            _interstitial = null;
            _rewarded = null;
            _banner = null;

            IsInitialized = false;
        }
    }
}
