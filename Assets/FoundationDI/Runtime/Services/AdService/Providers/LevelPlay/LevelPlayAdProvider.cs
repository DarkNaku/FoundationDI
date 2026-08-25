using System;
using System.Collections.Generic;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // LevelPlay SDK 초기화 + 세 어댑터(전면/보상/배너)의 조립 지점.
    // 정책(재시도/쿨다운/보상 래치)은 전혀 모른다 — Ads/ 계층이 그 역할이다.
    public class LevelPlayAdProvider : IAdProvider
    {
        // ---- SDK 전역(프로세스 수명) 상태 ----------------------------------------------
        //
        // LevelPlayAdProvider는 AdService 인스턴스마다 새로 만들어질 수 있지만 LevelPlay 자신은
        // 정적 클래스라 초기화가 프로세스 전역이다. 그래서 아래 셋은 static이어야 한다
        // (AppLovinAdProvider._initializeRequested와 같은 이유).

        // LevelPlay.Init을 우리가 이미 불렀는지. 두 번째 provider가 또 부르는 것을 막는다.
        private static bool _initializeRequested;

        // 초기화 결과 래치. LevelPlay에는 MaxSdk.IsInitialized()에 해당하는 조회 API가 없어서
        // (ILevelPlaySdk에 그런 메서드가 없다) "이미 끝났는지"를 알 방법이 이벤트를 붙잡아
        // 기록해 두는 것뿐이다. 기록하지 않으면 초기화가 이미 끝난 뒤에 만들어진 provider가
        // 다시는 오지 않는 콜백을 영원히 기다린다.
        //
        // bool? 하나가 아니라 volatile bool 둘인 이유: 이 값들은 SDK 콜백 스레드에서 쓰이고
        // 메인 스레드에서 읽힌다. Nullable<bool>은 필드 두 개(hasValue/value)라 한 번의 쓰기로
        // 원자적이지 않다. 완료 플래그를 나중에 쓰면 volatile 쓰기 순서 보장으로 "완료됐는데
        // 결과가 아직 안 보이는" 상태가 생기지 않는다.
        private static volatile bool _initSucceeded;
        private static volatile bool _initCompleted;

        // 초기화 완료를 인스턴스들에게 알리는 내부 팬아웃. LevelPlay.OnInitSuccess/OnInitFailed를
        // 인스턴스마다 직접 구독하지 않는 이유는, 그 구독을 반드시 씬 로드 **전에** 걸어야
        // 하기 때문이다(LevelPlayInstaller 주석 참고). provider 인스턴스는 그보다 훨씬 늦게
        // 만들어진다.
        private static event Action<bool> InitCompleted;

        // ---- 인스턴스 상태 --------------------------------------------------------------

        private readonly IAdDispatcher _dispatcher;

        // InitializeAsync가 여러 번 겹쳐 불려도(AdService는 상위에서 재진입을 막지만 이 클래스는
        // public API라 스스로도 안전해야 한다) 구독을 잃지 않도록 진행 중인 (핸들러, completion)
        // 쌍을 전부 들고 있는다. 핸들러는 발화되면 스스로 이 목록에서 빠지고, Dispose()는 남은
        // 항목을 전부 구독 해제하고 false로 깨운다.
        private readonly List<(Action<bool> Handler, AwaitableCompletionSource<bool> Source)>
            _pendingInitializations = new();

        private bool _isDisposed;

        // 이 문자열이 AdImpression.AdPlatform으로 나가 Firebase의 ad_platform이 된다.
        public string Name => "LevelPlay";

        // LevelPlay의 동의 플로우(LevelPlayPrivacySettings.SetGDPRConsent/SetCCPA/SetCOPPA)는
        // 이번 범위 밖이다 — IAdConsent가 요구하는 것은 "동의 폼을 띄우고 CanRequestAds를
        // 갱신하는" CMP 수준의 계약인데, LevelPlay가 제공하는 것은 이미 받은 동의 값을 SDK에
        // 통보하는 세터뿐이라 폼을 띄우는 주체(UMP/CMP)가 따로 필요하다. 억지로 매핑하면
        // "RequestAsync를 불렀는데 아무 폼도 안 뜨고 true가 온다"가 되므로, AppLovin provider와
        // 동일하게 no-op seam을 그대로 노출한다.
        public IAdConsent Consent { get; } = new NoopAdConsent();

        // provider 전역 임프레션 경로. IAdProvider의 주석은 이 이벤트의 존재 이유로 LevelPlay를
        // 지목하지만("임프레션 데이터가 광고 객체가 아니라 SDK 전역 이벤트 하나로 온다"),
        // 그것은 구버전 서술이다. 9.5.1에서는 전면/보상/배너 각 광고 객체가 자기
        // OnAdImpressionDataReady를 갖고 있고(ILevelPlayInterstitialAd.cs:55,
        // ILevelPlayRewardedAd.cs:55, ILevelPlayBannerAd.cs) 세 어댑터가 그것을 구독해
        // 어댑터별 Paid로 흘린다. 반대로 전역 LevelPlay.OnImpressionDataReady는 [Obsolete]가
        // 붙어 있고 그 메시지가 "각 광고 인스턴스의 OnAdImpressionDataReady를 쓰라"고
        // 명시한다(Runtime/Api/LevelPlay.cs의 OnImpressionDataReady 선언).
        //
        // 둘을 함께 구독하면 같은 임프레션이 두 경로로 올라와 수익이 이중 계상된다. 어댑터별
        // 경로가 모든 포맷을 덮으므로 전역 경로는 쓰지 않는다 — AppLovin과 같은 no-op이다.
        public event Action<AdImpression> ImpressionPaid { add { } remove { } }

        public LevelPlayAdProvider(IAdDispatcher dispatcher)
        {
            // 어댑터들이 임프레션 콜백을 메인 스레드로 마샬링하는 데 쓴다
            // (LevelPlayFullScreenAdapter.OnImpressionDataReady 주석 참고).
            // AppLovin provider가 dispatcher를 받기만 하고 쓰지 않았던 것과 달리, LevelPlay는
            // SDK에게 마샬링을 지시하는 스위치가 없어 실제로 필요하다.
            _dispatcher = dispatcher;
        }

        public Awaitable<bool> InitializeAsync(AdProviderContext context)
        {
            var source = new AwaitableCompletionSource<bool>();

            if (_isDisposed)
            {
                source.TrySetResult(false);
                return source.Awaitable;
            }

            // 에디터에서는 LevelPlay가 AfterSceneLoad에 자기 정적 이벤트를 전부 null로
            // 되돌린다(LevelPlay.ResetStaticsOnLoad, #if UNITY_EDITOR). 그러면 설치자가
            // BeforeSceneLoad에 걸어 둔 래치 구독도 함께 날아간다. 여기서 한 번 더 붙여
            // 복구한다 — SDK의 add 접근자가 중복 구독을 스스로 걸러내므로(LevelPlay.cs의
            // OnInitSuccess add에서 GetInvocationList().Contains 검사) 여러 번 불러도 안전하다.
            InstallInitLatch();

            // 이미 초기화가 끝난 뒤라면 콜백은 다시 오지 않는다. 래치가 없으면 여기서
            // 영원히 기다리게 된다.
            if (_initCompleted)
            {
                source.TrySetResult(_initSucceeded);
                return source.Awaitable;
            }

            ApplyContext(context);

            Action<bool> handler = null;
            handler = success =>
            {
                // 초기화 콜백의 스레드는 플랫폼마다 다르다. Android는 ThreadUtil.Post로
                // 메인 스레드에 넘기지만(Runtime/Platforms/Android/UnityLevelPlayInitListener.cs),
                // iOS는 MonoPInvokeCallback에서 곧바로 발화한다
                // (Runtime/Platforms/iOS/IosLevelPlaySdk.cs:117~127 — 광고 수명주기 콜백과 달리
                // ThreadUtil을 거치지 않는다). AwaitableCompletionSource는 메인 스레드 전제이고
                // SetResult가 대기 중이던 이어달리기(AdService.InitializeAsync 이후 전부)를
                // 그 자리에서 재개시키므로 반드시 마샬링한다.
                _dispatcher.Post(() =>
                {
                    InitCompleted -= handler;
                    _pendingInitializations.RemoveAll(p => p.Handler == handler);
                    source.TrySetResult(success);
                });
            };

            _pendingInitializations.Add((handler, source));
            InitCompleted += handler;

            // appKey가 비어 있으면 Init을 부르지 않고 래치만 기다린다. LevelPlay는 프로젝트
            // 설정(LevelPlayMediationSettings.EnableIronsourceSDKInitAPI)만으로도 스스로
            // 초기화할 수 있어서(Runtime/Utilities/LevelPlayAutoInitializer.cs), 키 없이
            // Init(null)을 부르면 SDK가 경고만 찍고 아무 콜백도 내지 않아 대기가 헛돈다.
            if (!_initializeRequested && !string.IsNullOrEmpty(context.AppKey))
            {
                _initializeRequested = true;
                LevelPlay.Init(context.AppKey);
            }

            return source.Awaitable;
        }

        // AdProviderContext의 세 설정을 LevelPlay에 반영한다. Init 호출 전에 세팅해야 하는 값이
        // 있어 여기서 한 번에 처리한다. 매 호출마다 현재 값을 그대로 반영한다(끄는 것도 가능해야
        // 한다 — 한 번 켜면 다시 끌 수 없는 API로 두지 않는다).
        private static void ApplyContext(AdProviderContext context)
        {
            // LevelPlay 자신의 상세 로그는 컴파일 심볼
            // (ENABLE_UNITY_SERVICES_LEVELPLAY_VERBOSE_LOGGING)로 게이트돼 있어 런타임에 켤 수
            // 없다. 런타임 토글로 존재하는 가장 가까운 것이 미디에이션 어댑터 디버그 로그이며,
            // LevelPlay 자신의 개발자 설정도 EnableAdapterDebug를 같은 API에 연결한다
            // (LevelPlayAutoInitializer.InitializeWithSettings).
            LevelPlay.SetAdaptersDebug(context.VerboseLogging);

            // context.TestMode / TestDeviceIds에 대응하는 LevelPlay API가 없다. LevelPlay는
            // 테스트 노출을 코드가 아니라 대시보드(Ad Unit의 테스트 설정)와 Test Suite로 다룬다.
            // LaunchTestSuite()가 있긴 하지만 그건 전면 UI를 띄우는 개발자 도구라 "테스트 모드"
            // 플래그의 대응물이 아니다. 잘못된 API에 억지로 매핑하느니 미사용이 정직하다 —
            // 다만 설정해 놓고 아무 일도 안 일어나는 상황은 알려 준다.
            if (!context.VerboseLogging) return;

            if (context.TestMode)
            {
                Debug.Log("[AdService] LevelPlay는 AdProviderContext.TestMode를 쓰지 않는다. " +
                          "테스트 노출은 LevelPlay 대시보드의 광고 단위 설정이나 Test Suite로 구성한다.");
            }

            if (context.TestDeviceIds != null && context.TestDeviceIds.Count > 0)
            {
                Debug.Log("[AdService] LevelPlay는 AdProviderContext.TestDeviceIds를 쓰지 않는다. " +
                          "테스트 기기는 LevelPlay 대시보드에 등록한다.");
            }
        }

        // LevelPlay.OnInitSuccess/OnInitFailed를 래치에 연결한다. LevelPlayInstaller가
        // BeforeSceneLoad에서 한 번, InitializeAsync가 방어적으로 한 번 더 부른다.
        internal static void InstallInitLatch()
        {
            LevelPlay.OnInitSuccess += OnSdkInitSuccess;
            LevelPlay.OnInitFailed += OnSdkInitFailed;
        }

        // 테스트/도메인 리로드 비활성화 대비. LevelPlayInstaller가 SubsystemRegistration에서 부른다.
        internal static void ResetInitLatch()
        {
            LevelPlay.OnInitSuccess -= OnSdkInitSuccess;
            LevelPlay.OnInitFailed -= OnSdkInitFailed;

            _initCompleted = false;
            _initSucceeded = false;
            _initializeRequested = false;
            InitCompleted = null;
        }

        // 결과를 먼저 쓰고 완료 플래그를 나중에 쓴다. volatile 쓰기는 재배치되지 않으므로
        // _initCompleted가 보이는 스레드에서는 _initSucceeded도 이미 보인다.
        private static void OnSdkInitSuccess(LevelPlayConfiguration configuration)
        {
            _initSucceeded = true;
            _initCompleted = true;
            InitCompleted?.Invoke(true);
        }

        private static void OnSdkInitFailed(LevelPlayInitError error)
        {
            Debug.LogWarning($"[AdService] LevelPlay 초기화 실패: {error}");

            _initSucceeded = false;
            _initCompleted = true;
            InitCompleted?.Invoke(false);
        }

        public IFullScreenAdapter CreateInterstitial(string adUnitId)
        {
            ThrowIfDisposed();
            return new LevelPlayFullScreenAdapter(AdFormat.Interstitial, adUnitId, _dispatcher);
        }

        public IFullScreenAdapter CreateRewarded(string adUnitId)
        {
            ThrowIfDisposed();
            return new LevelPlayFullScreenAdapter(AdFormat.Rewarded, adUnitId, _dispatcher);
        }

        public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options)
        {
            ThrowIfDisposed();
            return new LevelPlayBannerAdapter(adUnitId, options, _dispatcher);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(LevelPlayAdProvider));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 초기화 도중 Dispose되는 것은 흔한 일이다(예: 부트스트랩 취소). 여기서 완료시켜
            // 주지 않으면 AdService.InitializeAsync가 영원히 돌아오지 않고 그 대기자들이 함께
            // 멈춘다.
            //
            // 구독 해제 + 리스트 비우기를 통지(TrySetResult)보다 먼저 끝낸다. TrySetResult는
            // 대기 중이던 이어달리기를 동기적으로 재개시킬 수 있는데, 그 이어달리기가 예외를
            // 던지면 — 정리와 통지를 한 루프에 같이 뒀다면 — 나머지 항목의 구독 해제가 통째로
            // 건너뛰어져 이미 Dispose된 provider를 정적 이벤트가 계속 참조하게 된다
            // (AppLovinAdProvider.Dispose와 같은 순서).
            var pending = new List<(Action<bool> Handler, AwaitableCompletionSource<bool> Source)>(
                _pendingInitializations);

            foreach (var p in pending) InitCompleted -= p.Handler;
            _pendingInitializations.Clear();

            foreach (var p in pending)
            {
                try { p.Source.TrySetResult(false); }
                catch (Exception e) { Debug.LogException(e); }
            }

            // LevelPlay 자신은 종료 API가 없다(ILevelPlaySdk에 shutdown/deinit이 없다).
            // 광고 객체의 파기는 각 어댑터의 Dispose가 담당한다.
        }
    }
}
