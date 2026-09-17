using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Apple의 ATTrackingManager.AuthorizationStatus를 그대로 옮긴 것이다.
    // 값을 캐스팅으로 맞추지 않는다 — 바인딩 enum과 순서가 같아도 어댑터가 명시적으로
    // 번역한다(SDK가 값을 바꾸면 조용히 어긋나는 대신 컴파일에서 걸리도록).
    public enum TrackingAuthorizationStatus
    {
        // 아직 묻지 않았다. 이 상태에서만 다이얼로그가 뜬다.
        NotDetermined,

        // 기기 정책(자녀 보호 등)으로 허용될 수 없다. 다이얼로그는 뜨지 않는다.
        Restricted,

        Denied,
        Authorized,
    }

    // IDFA 접근 권한(iOS 14+ App Tracking Transparency)을 묻는 seam.
    //
    // IAdConsent와 다른 축이다. IAdConsent는 광고 네트워크가 요구하는 개인정보 동의
    // (GDPR/UMP/T&P Flow)를 다루고 provider마다 구현이 다르지만, 이쪽은 OS 권한이라
    // provider와 무관하게 하나뿐이다. 그래서 IAdProvider에 매달지 않고 별도로 둔다.
    public interface ITrackingAuthorization
    {
        // 이 플랫폼에서 물어볼 수 있는가. iOS 실기가 아니면 false다.
        bool IsSupported { get; }

        TrackingAuthorizationStatus Status { get; }

        // 필요하면 OS 다이얼로그를 띄우고 사용자가 답할 때까지 기다린다.
        // 물어볼 수 없는 상태면 현재 Status로 즉시 완료된다 — 절대 영원히 매달리지 않는다.
        Awaitable<TrackingAuthorizationStatus> RequestAsync();
    }
}
