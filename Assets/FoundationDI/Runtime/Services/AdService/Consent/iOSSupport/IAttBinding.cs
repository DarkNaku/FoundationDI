using Unity.Advertisement.IosSupport;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // ios-support 바인딩(ATTrackingStatusBinding)을 가리는 seam. 바인딩이 정적 클래스라
    // 그대로는 요청 흐름을 EditMode에서 검증할 수 없다.
    internal interface IAttBinding
    {
        bool IsSupported { get; }

        TrackingAuthorizationStatus Status { get; }

        // 파라미터 없는 요청만 둔다 — 콜백 오버로드는 1.2.0에만 있다(AttTrackingAuthorization 참고).
        void Request();
    }

    internal class IosSupportAttBinding : IAttBinding
    {
        // 바인딩은 에디터·안드로이드에서 아무것도 하지 않는다 — GetAuthorizationTrackingStatus는
        // 늘 NOT_DETERMINED를 돌려주고 RequestAuthorizationTracking은 네이티브를 부르지도
        // 않는다. 그래서 실기에서만 지원한다고 답한다. 이 값이 false면 AdService는 요청을
        // 건너뛴다.
        public bool IsSupported => Application.platform == RuntimePlatform.IPhonePlayer;

        public TrackingAuthorizationStatus Status =>
            Map(ATTrackingStatusBinding.GetAuthorizationTrackingStatus());

        public void Request() => ATTrackingStatusBinding.RequestAuthorizationTracking();

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
