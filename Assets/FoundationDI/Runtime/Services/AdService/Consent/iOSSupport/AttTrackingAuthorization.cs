using Unity.Advertisement.IosSupport;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // iOS 14 Advertising Support 패키지(com.unity.ads.ios-support)의 ATTrackingStatusBinding을
    // ITrackingAuthorization으로 감싼다.
    //
    // **완료 콜백을 쓰지 않고 상태를 폴링한다.** 1.2.0에는
    // RequestAuthorizationTracking(RequestAuthorizationTrackingCompleteHandler)가 있지만
    // 1.0.1에는 없고, 레지스트리가 latest로 가리키는 쪽은 1.0.1이다(1.2.0보다 뒤에
    // 게시됐다 — 2021-11 vs 2025-10). 두 버전의 asmdef 이름이 같아 심볼은 어느 쪽이든
    // 켜지므로, 콜백 오버로드를 부르면 Package Manager에서 그냥 설치한 사람은 컴파일조차
    // 되지 않는다. 파라미터 없는 요청과 상태 조회는 1.0.0부터 모든 버전에 있다.
    //
    // 폴링이 오히려 더 안전하기도 하다. 콜백 경로는 바인딩이 완료 델리게이트를 정적 필드
    // 하나에 담아 두고 이미 진행 중이면 InvalidOperationException을 던지며, 네이티브에서
    // 올라오는 콜백이라 메인 스레드 보장도 없다. 폴링은 디스패처 펌프 위에서만 도니까
    // 그 두 가지가 모두 사라진다.
    public class AttTrackingAuthorization : ITrackingAuthorization
    {
        // 다이얼로그가 끝내 뜨지 않는 경우를 대비한 상한이다. iOS는 앱이 아직 활성
        // (UIApplicationStateActive)이 아닐 때 요청하면 다이얼로그를 띄우지도, 상태를
        // 바꾸지도 않고 조용히 무시한다. 여기서 풀어 주지 않으면 그 한 번의 타이밍 사고로
        // AdService.InitializeAsync가 영원히 완료되지 않아 광고가 통째로 죽는다.
        //
        // 사용자가 이 시간을 넘겨 답해도 손해는 없다 — 초기화를 계속 진행할 뿐이고,
        // 뒤늦게 확정된 상태는 SDK가 각자 다시 읽는다.
        private const float TimeoutSeconds = 60f;

        private readonly IAdDispatcher _dispatcher;

        private AwaitableCompletionSource<TrackingAuthorizationStatus> _pending;
        private System.IDisposable _timeout;
        private System.IDisposable _poll;

        public AttTrackingAuthorization(IAdDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        // 바인딩은 에디터·안드로이드에서 아무것도 하지 않는다 — GetAuthorizationTrackingStatus는
        // 늘 NOT_DETERMINED를 돌려주고 RequestAuthorizationTracking은 네이티브를 부르지도
        // 않는다. 그래서 실기에서만 지원한다고 답한다. 이 값이 false면 AdService는 요청을
        // 건너뛴다.
        public bool IsSupported => Application.platform == RuntimePlatform.IPhonePlayer;

        public TrackingAuthorizationStatus Status =>
            Map(ATTrackingStatusBinding.GetAuthorizationTrackingStatus());

        public Awaitable<TrackingAuthorizationStatus> RequestAsync()
        {
            // 이미 진행 중인 요청에 편승시키지 않고 현재 상태로 즉시 답한다. AdService는
            // 초기화당 한 번만 부르므로 이 경로는 게임 코드가 직접 부른 경우뿐이다.
            if (!IsSupported || _pending != null) return Completed(Status);

            // 펌프가 없으면 상태가 바뀌는 것을 볼 방법이 없다. 요청만 띄우고 현재 상태로
            // 끝낸다 — 여기서 대기시키면 아무도 깨워 주지 않는다.
            if (_dispatcher == null)
            {
                ATTrackingStatusBinding.RequestAuthorizationTracking();
                return Completed(Status);
            }

            var source = new AwaitableCompletionSource<TrackingAuthorizationStatus>();

            _pending = source;
            _timeout = _dispatcher.Delay(TimeoutSeconds, OnTimeout);

            ATTrackingStatusBinding.RequestAuthorizationTracking();

            // 다음 프레임부터 본다. 같은 프레임에 보면 사용자가 아직 답하지 않은 것이
            // 당연하므로 한 번 헛돌 뿐이다.
            _poll = _dispatcher.NextFrames(1, Poll);

            return source.Awaitable;
        }

        // 디스패처 펌프 위에서만 돈다 — 즉 항상 메인 스레드다.
        private void Poll()
        {
            _poll = null;

            if (_pending == null) return;

            var status = Status;

            // NOT_DETERMINED를 벗어나는 순간이 곧 사용자가 답한 순간이다. 나머지 세 값은
            // 전부 확정 상태라 더 기다릴 것이 없다.
            if (status != TrackingAuthorizationStatus.NotDetermined)
            {
                Complete(status);
                return;
            }

            _poll = _dispatcher.NextFrames(1, Poll);
        }

        private void OnTimeout()
        {
            if (_pending == null) return;

            Debug.LogWarning($"[AdService] ATT 권한이 {TimeoutSeconds}초 안에 확정되지 않았다. " +
                             "현재 상태로 초기화를 계속한다.");

            Complete(Status);
        }

        private void Complete(TrackingAuthorizationStatus status)
        {
            var source = _pending;

            if (source == null) return;

            _pending = null;

            _timeout?.Dispose();
            _timeout = null;

            _poll?.Dispose();
            _poll = null;

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
