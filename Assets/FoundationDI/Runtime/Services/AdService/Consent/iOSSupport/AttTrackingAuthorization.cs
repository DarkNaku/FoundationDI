using System;
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
    //
    // **요청은 앱이 Active일 때만 보낸다.** iOS 15부터 requestTrackingAuthorization은
    // UIApplicationStateActive가 아니면 팝업도 오류도 없이 notDetermined로 끝난다. 그런데
    // Unity 6.3 트램펄린은 UIScene 라이프사이클에서 첫 씬을 sceneDidBecomeActive보다 먼저
    // 로드하므로, 첫 씬 Awake에서 부른 InitializeAsync가 바로 그 구간에서 요청을 보낸다.
    // 그래서 흐름이 이렇다:
    //
    //   Active 대기(매 프레임) → 안정화 대기 → 요청 → 폴링
    //     → 팝업 없이 시간이 지나도록 NotDetermined면 무시된 것으로 보고 점증 간격으로 재요청
    //     → Inactive를 거쳐(다른 권한 팝업 등) Active로 돌아왔는데 NotDetermined여도 재요청
    //
    // 재요청은 안전하다. iOS는 이미 답한 뒤의 요청에는 팝업을 띄우지 않는다.
    public class AttTrackingAuthorization : ITrackingAuthorization
    {
        // 어떤 경우에도 InitializeAsync가 영원히 멈추지 않게 하는 상한. Active 대기와 재요청을
        // 모두 포함한다. 사용자가 이 시간을 넘겨 답해도 손해는 없다 — 초기화를 계속 진행할
        // 뿐이고, 뒤늦게 확정된 상태는 SDK가 각자 다시 읽는다.
        //
        // 이 시간은 디스패처 펌프(AdServiceRunner.Update의 Time.unscaledDeltaTime)로 센다.
        // iOS 트램펄린은 applicationWillResignActive에서 UnityPause(1)로 플레이어 루프를
        // 멈추므로, 팝업이 떠 있거나 백그라운드에 있는 동안에는 펌프가 돌지 않아 시간이
        // 흐르지 않는다. 의도한 동작이다 — 사용자가 팝업을 읽는 시간에 타임아웃이 끼어들면
        // 안 된다. 재개 첫 프레임의 delta에 멈춘 시간이 실리는지는 엔진 내부라 보장하지
        // 않지만, 어느 쪽이어도 결과는 맞다. 타임아웃은 그 순간의 Status로 완료하므로 이미
        // 답을 받았다면 확정값이 그대로 나간다.
        internal const float TimeoutSeconds = 60f;

        // Active가 된 직후는 장면 전환이 끝나는 중이라 바로 요청하면 다시 버려질 수 있다.
        internal const float SettleSeconds = 0.5f;

        // 요청 후 이 시간 동안 앱이 한 번도 Inactive로 내려가지 않았다면(= 팝업이 뜨지
        // 않았다면) 무시된 것으로 본다. 팝업이 뜨면 트램펄린이 루프를 멈추므로 이 타이머도
        // 함께 멈춰, 사용자가 읽는 동안 오판하지 않는다.
        internal const float IgnoreWindowSeconds = 1.5f;

        internal const int MaxRetries = 3;

        internal static readonly float[] RetryDelaySeconds = { 1f, 2f, 4f };

        private readonly IAdDispatcher _dispatcher;
        private readonly IAttBinding _binding;
        private readonly IAppActivity _activity;

        private AwaitableCompletionSource<TrackingAuthorizationStatus> _pending;
        private IDisposable _timeout;
        private IDisposable _step;
        private IDisposable _ignoreWindow;

        private int _requestCount;
        private int _resignCountAtRequest;

        public AttTrackingAuthorization(IAdDispatcher dispatcher)
            : this(dispatcher, new IosSupportAttBinding(), new NativeAppActivity())
        {
        }

        internal AttTrackingAuthorization(IAdDispatcher dispatcher, IAttBinding binding, IAppActivity activity)
        {
            _dispatcher = dispatcher;
            _binding = binding;
            _activity = activity;
        }

        public bool IsSupported => _binding.IsSupported;

        public TrackingAuthorizationStatus Status => _binding.Status;

        public Awaitable<TrackingAuthorizationStatus> RequestAsync()
        {
            // 이미 진행 중인 요청에 편승시키지 않고 현재 상태로 즉시 답한다. AdService는
            // 초기화당 한 번만 부르므로 이 경로는 게임 코드가 직접 부른 경우뿐이다.
            if (!IsSupported || _pending != null) return Completed(Status);

            // 펌프가 없으면 Active를 기다릴 수도, 상태가 바뀌는 것을 볼 수도 없다. 요청만
            // 띄우고 현재 상태로 끝낸다 — 여기서 대기시키면 아무도 깨워 주지 않는다.
            if (_dispatcher == null)
            {
                _binding.Request();
                return Completed(Status);
            }

            var source = new AwaitableCompletionSource<TrackingAuthorizationStatus>();

            _pending = source;
            _requestCount = 0;
            _timeout = _dispatcher.Delay(TimeoutSeconds, OnTimeout);

            WaitForActive();

            return source.Awaitable;
        }

        private void WaitForActive()
        {
            if (_pending == null) return;

            Debug.Log("[AdService] ATT Active 대기 시작");

            CheckActive();
        }

        // 디스패처 펌프 위에서만 돈다(첫 호출만 RequestAsync 안) — 즉 항상 메인 스레드다.
        private void CheckActive()
        {
            _step = null;

            if (_pending == null) return;

            // 지난 세션에 이미 답했거나, 기다리는 사이 다른 경로로 답이 들어온 경우.
            if (TryCompleteDetermined()) return;

            if (!_activity.IsActive)
            {
                _step = _dispatcher.NextFrames(1, CheckActive);
                return;
            }

            Debug.Log($"[AdService] ATT Active 대기 종료 — {SettleSeconds}초 안정화 후 요청한다");

            _step = _dispatcher.Delay(SettleSeconds, SendRequest);
        }

        private void SendRequest()
        {
            _step = null;

            if (_pending == null) return;

            if (TryCompleteDetermined()) return;

            if (!_activity.IsActive)
            {
                Debug.Log("[AdService] ATT 안정화 중 다시 Inactive가 됐다 — Active를 다시 기다린다");
                WaitForActive();
                return;
            }

            _requestCount++;
            _resignCountAtRequest = _activity.ResignActiveCount;

            Debug.Log($"[AdService] ATT {_requestCount}번째 요청");

            _binding.Request();

            _ignoreWindow = _dispatcher.Delay(IgnoreWindowSeconds, OnIgnoreWindowElapsed);

            // 다음 프레임부터 본다. 같은 프레임에 보면 사용자가 아직 답하지 않은 것이
            // 당연하므로 한 번 헛돌 뿐이다.
            _step = _dispatcher.NextFrames(1, Observe);
        }

        private void Observe()
        {
            _step = null;

            if (_pending == null) return;

            if (TryCompleteDetermined()) return;

            // Inactive를 거쳐 Active로 돌아왔는데도 답이 없다. ATT 팝업이었다면 상태가 확정됐을
            // 것이므로, 다른 시스템 팝업과 겹쳐 ATT가 버려진 경우다("Concurrent requests aren't
            // preserved by iOS").
            if (_activity.ResignActiveCount != _resignCountAtRequest && _activity.IsActive)
            {
                Retry("Inactive를 거쳐 돌아왔는데 NotDetermined");
                return;
            }

            _step = _dispatcher.NextFrames(1, Observe);
        }

        private void OnIgnoreWindowElapsed()
        {
            _ignoreWindow = null;

            if (_pending == null) return;

            // 카운트가 바뀌었으면 팝업이 떴다는 뜻이다. 그 경우는 Observe가 Active 복귀를 보고 판정한다.
            if (_activity.ResignActiveCount != _resignCountAtRequest) return;

            if (TryCompleteDetermined()) return;

            Retry($"{IgnoreWindowSeconds}초 동안 팝업 없이 NotDetermined");
        }

        private void Retry(string reason)
        {
            _step?.Dispose();
            _step = null;

            _ignoreWindow?.Dispose();
            _ignoreWindow = null;

            var retryIndex = _requestCount - 1;

            if (retryIndex >= MaxRetries)
            {
                // 남은 타임아웃까지 붙잡고 있어도 팝업이 뜰 근거가 없다. 광고 초기화만 늦어진다.
                Debug.LogWarning($"[AdService] ATT 무시 판정({reason}) — 재요청 {MaxRetries}회를 모두 썼다. " +
                                 "현재 상태로 초기화를 계속한다.");
                Complete(Status);
                return;
            }

            var delay = RetryDelaySeconds[retryIndex];

            Debug.Log($"[AdService] ATT 무시 판정({reason}) — {delay}초 뒤 재요청한다");

            _step = _dispatcher.Delay(delay, WaitForActive);
        }

        private bool TryCompleteDetermined()
        {
            var status = Status;

            // NOT_DETERMINED를 벗어나는 순간이 곧 사용자가 답한 순간이다. 나머지 세 값은
            // 전부 확정 상태라 더 기다릴 것이 없다.
            if (status == TrackingAuthorizationStatus.NotDetermined) return false;

            Complete(status);
            return true;
        }

        private void OnTimeout()
        {
            _timeout = null;

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

            _step?.Dispose();
            _step = null;

            _ignoreWindow?.Dispose();
            _ignoreWindow = null;

            Debug.Log($"[AdService] ATT 최종 상태: {status} (요청 {_requestCount}회)");

            source.TrySetResult(status);
        }

        private static Awaitable<TrackingAuthorizationStatus> Completed(TrackingAuthorizationStatus status)
        {
            var source = new AwaitableCompletionSource<TrackingAuthorizationStatus>();
            source.SetResult(status);
            return source.Awaitable;
        }
    }
}
