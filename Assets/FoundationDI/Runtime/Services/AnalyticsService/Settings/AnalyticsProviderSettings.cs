using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 어댑터 고유 설정의 공통 기반. 코어는 이 타입의 "존재"만 알고 내용은 전혀 모른다.
    //
    // Firebase는 google-services.json이 설정을 대신하므로 어댑터가 들고 다닐 값이 없지만,
    // Adjust는 앱 토큰과 이름→토큰 매핑표가 필요하다. 그 값들을 AnalyticsServiceSettings에
    // 직접 넣으면 두 가지가 깨진다 — 정책 계층이 토큰의 존재를 알게 되고(README 2.3이 금지한다),
    // 코어 설정이 SDK가 늘어날 때마다 필드로 부풀어 오른다.
    //
    // 대신 어댑터가 자기 SO를 정의해 이 클래스를 상속하고, 코어는 목록으로 들고 있다가
    // provider 생성 시점에 그대로 넘긴다. 어떤 항목이 자기 것인지는 어댑터가 타입으로 고른다
    // (AnalyticsProviderCreationContext.GetSettings<T>).
    //
    // 마커일 뿐 멤버가 없다. 코어가 읽을 수 있는 공통 필드를 하나라도 두는 순간 "코어는 내용을
    // 모른다"는 규칙에 예외가 생기고, 그 예외가 곧 두 번째 예외의 근거가 된다.
    public abstract class AnalyticsProviderSettings : ScriptableObject
    {
    }
}
