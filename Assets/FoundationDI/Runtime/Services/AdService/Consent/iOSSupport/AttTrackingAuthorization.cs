using System;
using Unity.Advertisement.IosSupport;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // iOS 14 Advertising Support 패키지(com.unity.ads.ios-support)의 ATTrackingStatusBinding을
    // ITrackingAuthorization으로 감싼다.
    public class AttTrackingAuthorization : ITrackingAuthorization
    {
        // 다이얼로그가 끝내 뜨지 않는 경우를 대비한 상한이다. iOS는 앱이 아직 활성
        // (UIApplicationStateActive)이 아닐 때 요청하면 다이얼로그를 띄우지도, 콜백을
        // 부르지도 않고 조용히 무시한다. 여기서 풀어 주지 않으면 그 한 번의 타이밍 사고로
        // AdService.InitializeAsync가 영원히 완료되지 않아 광고가 통째로 죽는다.
        //
        // 사용자가 이 시간을 넘겨 답해도 손해는 없다 — 초기화를 계속 진행할 뿐이고,
        // 뒤늦게 확정된 상태는 SDK가 각자 다시 읽는다.
        private const float TimeoutSeconds = 60f;

        private readonly IAdDispatcher _dispatcher;

        private AwaitableCompletionSource<TrackingAuthorizationStatus> _pending;
        private IDisposable _timeout;

        public AttTrackingAuthorization(IAdDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        // 바인딩은 에디터·안드로이드에서 아무것도 하지 않는다 — GetAuthorizationTrackingStatus는
        // 늘 NOT_DETERMINED를 돌려주고 RequestAuthorizationTracking은 콜백조차 부르지 않는다.
        // 그래서 실기에서만 지원한다고 답한다. 이 값이 false면 AdService는 요청을 건너뛴다.
        public bool IsSupported => Application.platform == RuntimePlatform.IPhonePlayer;

        public TrackingAuthorizationStatus Status =>
            Map(ATTrackingStatusBinding.GetAuthorizationTrackingStatus());

        public Awaitable<TrackingAuthorizationStatus> RequestAsync()
        {
            // 이미 진행 중인 요청에 편승시키지 않고 현재 상태로 즉시 답한다. 바인딩이
            // 콜백을 하나만 들고 있어 두 번째 요청은 어차피 예외가 되고, AdService는
            // 초기화당 한 번만 부르므로 이 경로는 게임 코드가 직접 부른 경우뿐이다.
            if (!IsSupported || _pending != null) return Completed(Status);

            var source = new AwaitableCompletionSource<TrackingAuthorizationStatus>();

            _pending = source;

            // 타임아웃을 요청보다 먼저 건다. 콜백이 같은 스택에서 돌아오는 구현이라면
            // 나중에 걸었을 때 이미 끝난 요청에 타이머가 남는다.
            _timeout = _dispatcher?.Delay(TimeoutSeconds, OnTimeout);

            try
            {
                ATTrackingStatusBinding.RequestAuthorizationTracking(OnRequestCompleted);
            }
            catch (Exception e)
            {
                // 바인딩은 이미 진행 중인 요청이 있으면 InvalidOperationException을 던진다.
                // 우리가 아니라 게임 코드가 먼저 불렀을 수 있다 — 그 경우 현재 상태로 답하고
                // 광고 초기화를 막지 않는다.
                Debug.LogWarning($"[AdService] ATT 권한 요청에 실패했다: {e.Message}");
                Complete(Status);
            }

            return source.Awaitable;
        }

        // 네이티브 콜백이다. Apple의 completion handler는 메인 스레드 보장이 없으므로
        // 반드시 마샬링한다 — LevelPlay의 임프레션 콜백과 같은 이유다.
        private void OnRequestCompleted(int status)
        {
            var mapped = Map((ATTrackingStatusBinding.AuthorizationTrackingStatus)status);

            if (_dispatcher == null) Complete(mapped);
            else _dispatcher.Post(() => Complete(mapped));
        }

        private void OnTimeout()
        {
            if (_pending == null) return;

            Debug.LogWarning($"[AdService] ATT 다이얼로그가 {TimeoutSeconds}초 안에 응답하지 않았다. " +
                             "현재 상태로 초기화를 계속한다.");

            Complete(Status);
        }

        private void Complete(TrackingAuthorizationStatus status)
        {
            var source = _pending;

            // 타임아웃 뒤에 늦게 도착한 콜백. 대기자는 이미 풀려났으므로 버린다.
            if (source == null) return;

            _pending = null;
            _timeout?.Dispose();
            _timeout = null;

            source.TrySetResult(status);
        }

        private static Awaitable<TrackingAuthorizationStatus> Completed(TrackingAuthorizationStatus status)
        {
            var source = new AwaitableCompletionSource<TrackingAuthorizationStatus>();
            source.SetResult(status);
            return source.Awaitable;
        }

        // 값이 같아도 캐스팅하지 않는다. 패키지가 enum 순서를 바꾸면 조용히 어긋나는 대신
        // 여기서 컴파일 에러가 나야 한다.
        private static TrackingAuthorizationStatus Map(ATTrackingStatusBinding.AuthorizationTrackingStatus status)
        {
            switch (status)
            {
                case ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED:
                    return TrackingAuthorizationStatus.Authorized;

                case ATTrackingStatusBinding.AuthorizationTrackingStatus.DENIED:
                    return TrackingAuthorizationStatus.Denied;

                case ATTrackingStatusBinding.AuthorizationTrackingStatus.RESTRICTED:
                    return TrackingAuthorizationStatus.Restricted;

                default:
                    return TrackingAuthorizationStatus.NotDetermined;
            }
        }
    }
}
