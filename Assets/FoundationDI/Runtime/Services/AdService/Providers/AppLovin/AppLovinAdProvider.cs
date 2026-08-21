using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AppLovin MAX SDK 초기화 + 세 어댑터(전면/보상/배너)의 조립 지점.
    // 정책(재시도/쿨다운/보상 래치)은 전혀 모른다 — Ads/ 계층이 그 역할이다.
    public class AppLovinAdProvider : IAdProvider
    {
        // MaxSdk.InitializeSdk()를 프로세스 수명 동안 두 번 이상 부르지 않기 위한 가드.
        // AppLovinAdProvider는 AdService 인스턴스마다 하나씩 새로 만들어질 수 있지만
        // (도메인 리로드, 테스트, 두 번째 AdService 등) MaxSdk 자신은 프로세스 전역
        // 정적 SDK라서 이 가드도 정적이어야 한다.
        private static bool _initializeRequested;

        private System.Action<MaxSdkBase.SdkConfiguration> _onSdkInitialized;
        private bool _isDisposed;

        public string Name => "AppLovin";

        // MAX용 CMP/UMP 동의 플로우는 이번 범위 밖이다(작업 지시서의 "만들 파일" 목록에
        // 없다) — Dummy provider와 동일하게 no-op seam을 그대로 노출한다.
        public IAdConsent Consent { get; } = new NoopAdConsent();

        // provider 전역 임프레션 경로. MAX의 ILRD는 광고 객체(포맷) 단위 정적 이벤트로
        // 오므로(OnAdRevenuePaidEvent) 어댑터별 Paid만으로 전부 커버된다 — LevelPlay와
        // 달리 여기로 흘릴 임프레션이 없다. Dummy와 같은 이유로 no-op.
        public event System.Action<AdImpression> ImpressionPaid { add { } remove { } }

        public AppLovinAdProvider(IAdDispatcher dispatcher)
        {
            // MAX Unity 플러그인은 네이티브 콜백을 MaxEventExecutor(Assets/MaxSdk/Scripts/
            // MaxEventExecutor.cs)로 이미 메인 스레드 큐잉한다 — MaxSdkCallbacks.cs의 모든
            // 이벤트가 ExecuteOnMainThread를 거쳐 발화되고, 실제 Invoke는 그 MonoBehaviour의
            // Update()에서만 일어난다(락으로 보호된 큐를 매 프레임 드레인). 그래서 이
            // 어댑터들은 IAdDispatcher.Post로 다시 감싸지 않는다 — 이미 메인 스레드인
            // 콜백을 한 번 더 감싸면 프레임 하나만큼의 지연을 매 콜백마다 얹는 것뿐이다.
            // dispatcher는 AdProviderCreationContext/IAdProvider 생성 규약을 맞추기 위해
            // 받기만 하고 쓰지 않는다.
        }

        public Awaitable<bool> InitializeAsync(AdProviderContext context)
        {
            var source = new AwaitableCompletionSource<bool>();

            // 테스트 기기 ID/verbose 로깅은 InitializeSdk() 호출 전에 세팅해야 반영된다.
            if (context.VerboseLogging) MaxSdk.SetVerboseLogging(true);

            if (context.TestDeviceIds != null && context.TestDeviceIds.Count > 0)
            {
                var ids = new string[context.TestDeviceIds.Count];
                for (var i = 0; i < ids.Length; i++) ids[i] = context.TestDeviceIds[i];
                MaxSdk.SetTestDeviceAdvertisingIdentifiers(ids);
            }

            // context.TestMode에 대응하는 MAX API가 없다. MAX는 "테스트 모드"를 불리언
            // 스위치가 아니라 대시보드/기기에 등록된 테스트 기기 광고 ID로만 판단한다
            // (Mediation Debugger도 마찬가지). 그래서 이 필드는 의도적으로 아무 데도
            // 쓰지 않는다 — 잘못된 API에 억지로 매핑하느니 미사용이 정직하다.

            // context.AppKey도 MAX에는 프로그래밍적으로 동작하는 대응이 없다.
            // MaxSdk.SetSdkKey(string)이 존재하긴 하지만 [Obsolete]이고, Android
            // 구현(MaxSdkAndroid.SetSdkKey)은 경고 로그만 남길 뿐 실제로 키를 설정하지
            // 않는 완전한 no-op이다(Assets/MaxSdk/Scripts/MaxSdkAndroid.cs 확인). MAX의
            // 공식 경로는 AppLovin Integration Manager가 AndroidManifest.xml/Info.plist에
            // 심는 것뿐이다. 그래서 여기서도 호출하지 않는다 — 호출해도 최소 한 플랫폼에서
            // 조용히 아무 일도 하지 않는 API에 기대는 것은 더 나쁘다.
            if (context.VerboseLogging && !string.IsNullOrEmpty(context.AppKey))
            {
                Debug.Log("[AdService] AppLovin MAX는 AdProviderContext.AppKey를 쓰지 않는다. " +
                         "SDK 키는 AppLovin Integration Manager(AndroidManifest.xml/Info.plist)로 설정한다.");
            }

            // 이미 초기화됐으면(예: 두 번째 AdService 인스턴스) OnSdkInitializedEvent는
            // 다시 오지 않는다 — 여기서 걸러내지 않으면 영원히 기다리게 된다.
            if (MaxSdk.IsInitialized())
            {
                source.TrySetResult(true);
                return source.Awaitable;
            }

            // InitializeSdk() 호출보다 먼저 구독해야 결과를 놓치지 않는다.
            _onSdkInitialized = _ =>
            {
                MaxSdkCallbacks.OnSdkInitializedEvent -= _onSdkInitialized;
                _onSdkInitialized = null;
                source.TrySetResult(true);
            };
            MaxSdkCallbacks.OnSdkInitializedEvent += _onSdkInitialized;

            if (!_initializeRequested)
            {
                _initializeRequested = true;
                MaxSdk.InitializeSdk();
            }

            return source.Awaitable;
        }

        public IFullScreenAdapter CreateInterstitial(string adUnitId) =>
            new AppLovinFullScreenAdapter(AdFormat.Interstitial, adUnitId);

        public IFullScreenAdapter CreateRewarded(string adUnitId) =>
            new AppLovinFullScreenAdapter(AdFormat.Rewarded, adUnitId);

        public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options) =>
            new AppLovinBannerAdapter(adUnitId, options);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_onSdkInitialized != null)
            {
                MaxSdkCallbacks.OnSdkInitializedEvent -= _onSdkInitialized;
                _onSdkInitialized = null;
            }
        }
    }
}
