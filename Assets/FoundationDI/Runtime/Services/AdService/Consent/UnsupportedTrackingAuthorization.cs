using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // iOS 14 Advertising Support 패키지가 없거나 iOS가 아닌 플랫폼에서 쓰는 기본 구현.
    //
    // null 대신 이 객체를 돌려주는 이유는, 게임 코드가 IAdService.TrackingAuthorization을
    // 플랫폼 분기 없이 읽을 수 있게 하기 위해서다. 안드로이드에도 "추적 권한"이라는 개념이
    // 없으므로 NotDetermined에 머무는 것이 정확한 답이다.
    public class UnsupportedTrackingAuthorization : ITrackingAuthorization
    {
        public bool IsSupported => false;

        public TrackingAuthorizationStatus Status => TrackingAuthorizationStatus.NotDetermined;

        public Awaitable<TrackingAuthorizationStatus> RequestAsync()
        {
            var source = new AwaitableCompletionSource<TrackingAuthorizationStatus>();
            source.SetResult(TrackingAuthorizationStatus.NotDetermined);
            return source.Awaitable;
        }
    }
}
