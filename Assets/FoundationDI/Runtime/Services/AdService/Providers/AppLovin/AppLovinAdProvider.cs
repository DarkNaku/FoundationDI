using System;
using System.Collections.Generic;
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

        // InitializeAsync가 여러 번 겹쳐 불려도(방어적 가드 — AdService는 이미 상위에서
        // 재진입을 막지만, 이 클래스는 public API라 스스로도 안전해야 한다) 구독을 잃어버리지
        // 않도록 진행 중인 (핸들러, completion) 쌍을 전부 들고 있는다. 핸들러는 발화되면
        // 스스로 이 목록에서 빠진다. Dispose()는 남은 항목을 전부 구독 해제하고 Failed로
        // 깨운다 — 예전에는 필드 하나에 마지막 핸들러만 담아서, 두 번째 호출이 첫 번째
        // 핸들러의 구독 해제 경로를 잃어버렸다(리뷰 지적).
        private readonly List<(Action<MaxSdkBase.SdkConfiguration> Handler, AwaitableCompletionSource<bool> Source)>
            _pendingInitializations = new();

        private bool _isDisposed;

        public string Name => "AppLovin";

        // MAX용 CMP/UMP 동의 플로우는 이번 범위 밖이다(작업 지시서의 "만들 파일" 목록에
        // 없다) — Dummy provider와 동일하게 no-op seam을 그대로 노출한다.
        public IAdConsent Consent { get; } = new NoopAdConsent();

        // provider 전역 임프레션 경로. MAX의 ILRD는 광고 객체(포맷) 단위 정적 이벤트로
        // 오므로(OnAdRevenuePaidEvent) 어댑터별 Paid만으로 전부 커버된다 — LevelPlay와
        // 달리 여기로 흘릴 임프레션이 없다. Dummy와 같은 이유로 no-op.
        public event Action<AdImpression> ImpressionPaid { add { } remove { } }

        public AppLovinAdProvider(IAdDispatcher dispatcher)
        {
            // 마샬링 책임은 InitializeAsync가 MaxSdkBase.InvokeEventsOnUnityMainThread를
            // 세팅해서 진다(아래 주석 참고). dispatcher는 AdProviderCreationContext/
            // IAdProvider 생성 규약을 맞추기 위해 받기만 하고 쓰지 않는다.
        }

        public Awaitable<bool> InitializeAsync(AdProviderContext context)
        {
            var source = new AwaitableCompletionSource<bool>();

            if (_isDisposed)
            {
                source.TrySetResult(false);
                return source.Awaitable;
            }

            // 반드시 이 메서드의 모든 진입 경로(이미 초기화된 경우 포함)보다 먼저 세팅한다.
            //
            // MAX Unity 플러그인은 대부분의 콜백을 MaxEventExecutor(Assets/MaxSdk/Scripts/
            // MaxEventExecutor.cs)로 메인 스레드 큐잉하지만, 전부는 아니다. MaxSdkCallbacks의
            // 네 InvokeEvent 오버로드는 각각 `if (ShouldInvokeInBackground(keepInBackground))
            // { evt(...); }`로 시작하고, 이 분기를 타면 그 자리(네이티브 콜백 스레드)에서
            // 곧바로 발화시킨다 — MaxEventExecutor를 아예 거치지 않는다
            // (MaxSdkCallbacks.cs:965~1053). `keepInBackground`는 네이티브가 이벤트마다
            // 실어 보내는 값인데, iOS 플러그인은 전면/보상 수익 콜백에서 명시적으로 참을
            // 준다 — `args[@"keepInBackground"] = @([adFormat isFullscreenAd]);`
            // (Assets/MaxSdk/AppLovin/Plugins/iOS/MAUnityAdManager.m:1005, didPayRevenueForAd).
            // 즉 **전면/보상의 OnAdRevenuePaidEvent는 기본값 그대로 두면 백그라운드(네이티브)
            // 스레드에서 발화된다.** 배너 수익은 영향받지 않는다(배너는 keepInBackground를
            // 안 준다). Android는 이 리포지토리에서 JVM 툴체인 없이 .aar 역디컴파일로만
            // 확인했지만 같은 리터럴(`keepInBackground`, `isMainThread`)과
            // `BackgroundCallbackProxy` 전달 경로가 나와 iOS와 동일하다고 간주한다.
            //
            // `ShouldInvokeInBackground`는 `MaxSdkBase.InvokeEventsOnUnityMainThread`
            // (`bool?`, 공개 세터)가 null이 아니면 그 값을 우선한다
            // (`!InvokeEventsOnUnityMainThread.Value`) — 이 필드는 벤더 SDK 어디에서도
            // 대입되지 않으므로(선언 하나, 읽는 곳 하나) 우리가 세팅하지 않으면 항상 null이고
            // `keepInBackground`가 그대로 이긴다. 여기서 true로 고정하면 모든 콜백(전면/보상
            // 수익 포함)이 예외 없이 MaxEventExecutor를 거친다 — Editor에서는 재현되지
            // 않는다(MaxSdkUnityEditor는 애초에 메인 스레드 코루틴으로 ForwardEvent를
            // 돌린다). 이 세팅 덕분에 IAdDispatcher.Post로 다시 감싸지 않아도 되는 원래
            // 주장은 유효하다 — 다만 "MAX가 알아서 마샬링한다"가 아니라 "이 provider가
            // MAX에게 마샬링하라고 명시적으로 지시했기 때문에" 유효한 것이다.
            MaxSdkBase.InvokeEventsOnUnityMainThread = true;

            // 테스트 기기 ID/verbose 로깅은 InitializeSdk() 호출 전에 세팅해야 반영된다.
            // 매 호출마다 현재 값을 그대로 반영한다(끄는 것도 가능해야 한다 — 한 번 켜면
            // 다시 끌 수 없는 API로 두지 않는다).
            MaxSdk.SetVerboseLogging(context.VerboseLogging);

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
            // MaxSdk.SetSdkKey(string)이 존재하고 [Obsolete]다 — iOS/Android 둘 다 실제로
            // 네이티브 setSdkKey를 호출하긴 한다(MaxSdkAndroid.cs:970의
            // `MaxUnityPluginClass.CallStatic("setSdkKey", sdkKey)`, MaxSdkiOS.cs의
            // `_MaxSetSdkKey`) — no-op은 아니다. 그래도 호출하지 않는다: AppLovin의 공식
            // 가이드가 "Integration Manager로 설정하라"고 명시적으로 대체를 권고하는
            // deprecated API이고, 이 경로를 쓰면 Integration Manager가 심어둔 값과 여기서
            // 넘긴 값 중 어느 쪽이 실제로 적용되는지가 초기화 순서/플랫폼에 따라 달라지는
            // 모호함만 늘어난다.
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
            Action<MaxSdkBase.SdkConfiguration> handler = null;
            handler = configuration =>
            {
                MaxSdkCallbacks.OnSdkInitializedEvent -= handler;
                _pendingInitializations.RemoveAll(p => p.Handler == handler);

                // MAX는 초기화 실패 콜백이 따로 없지만, SdkConfiguration은 성공 여부를
                // IsSuccessfullyInitialized로 알려준다 — 이 값을 무시하고 항상 true를
                // 돌려주면 AdService의 "초기화 실패 시 광고를 요청하지 않는다" 분기가
                // MAX에서는 죽은 코드가 된다.
                source.TrySetResult(configuration != null && configuration.IsSuccessfullyInitialized);
            };

            _pendingInitializations.Add((handler, source));
            MaxSdkCallbacks.OnSdkInitializedEvent += handler;

            if (!_initializeRequested)
            {
                _initializeRequested = true;
                MaxSdk.InitializeSdk();
            }

            return source.Awaitable;
        }

        public IFullScreenAdapter CreateInterstitial(string adUnitId)
        {
            ThrowIfDisposed();
            return new AppLovinFullScreenAdapter(AdFormat.Interstitial, adUnitId);
        }

        public IFullScreenAdapter CreateRewarded(string adUnitId)
        {
            ThrowIfDisposed();
            return new AppLovinFullScreenAdapter(AdFormat.Rewarded, adUnitId);
        }

        public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options)
        {
            ThrowIfDisposed();
            return new AppLovinBannerAdapter(adUnitId, options);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AppLovinAdProvider));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 초기화 도중 Dispose되는 것은 흔한 일이다(예: 부트스트랩 취소). 여기서 완료시켜
            // 주지 않으면 AdService.InitializeAsync가 영원히 돌아오지 않고, _initWaiters에
            // 편승한 모든 대기자가 함께 멈춘다(FullScreenAdUnit.Dispose가 대기 중인
            // ShowAsync를 Failed로 깨우는 것과 같은 이유).
            foreach (var pending in _pendingInitializations)
            {
                MaxSdkCallbacks.OnSdkInitializedEvent -= pending.Handler;
                pending.Source.TrySetResult(false);
            }
            _pendingInitializations.Clear();
        }
    }
}
