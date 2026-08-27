using System;
using System.Collections.Generic;
using AdjustSdk;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Adjust 어댑터 고유 설정. 코어(AnalyticsService)는 이 타입의 내용을 모른다 —
    // AnalyticsServiceSettings의 Provider Settings 목록에 끌어다 놓으면 생성 시점에
    // 어댑터에게 그대로 전달된다(AnalyticsProviderCreationContext.GetSettings<T>).
    //
    // Firebase에는 대응물이 없다. Firebase는 google-services.json이 설정을 대신하지만
    // Adjust는 앱 토큰과 "이름→토큰" 매핑표를 코드/에셋 쪽에서 들고 있어야 한다.
    [CreateAssetMenu(fileName = "AdjustAnalyticsSettings",
                     menuName = "FoundationDI/Adjust Analytics Settings")]
    public sealed class AdjustAnalyticsSettings : AnalyticsProviderSettings
    {
        // 게임이 LogEvent에 넘기는 이름 하나와 Adjust 대시보드가 발급한 토큰 하나의 짝.
        [Serializable]
        public struct EventTokenEntry
        {
            [Tooltip("게임 코드가 LogEvent에 넘기는 이름. 예: level_complete")]
            public string EventName;

            [Tooltip("Adjust 대시보드가 발급한 토큰. 예: abc123")]
            public string Token;
        }

        [Header("App")]
        [Tooltip("Adjust 대시보드의 Android 앱 토큰. Android와 iOS는 Adjust에서 서로 다른 앱이라 토큰도 다르다.")]
        [SerializeField] private string _androidAppToken;

        [Tooltip("Adjust 대시보드의 iOS 앱 토큰.")]
        [SerializeField] private string _iosAppToken;

        [Tooltip("Sandbox는 테스트 콘솔로만 흘러가고 어트리뷰션에 집계되지 않는다. 출시 빌드는 Production이어야 한다.")]
        [SerializeField] private AdjustEnvironment _environment = AdjustEnvironment.Production;

        [Tooltip("개발 빌드(Development Build)에서는 위 설정과 무관하게 Sandbox로 강제한다. " +
                 "Production인 채로 테스트해 실제 어트리뷰션 데이터를 오염시키는 사고를 막는다.")]
        [SerializeField] private bool _forceSandboxInDevelopmentBuild = true;

        [SerializeField] private AdjustLogLevel _logLevel = AdjustLogLevel.Info;

        [Tooltip("백그라운드에서도 전송을 시도한다. Adjust 기본값은 꺼짐이다.")]
        [SerializeField] private bool _sendInBackground;

        [Header("Events")]
        [Tooltip("LogEvent(name, ...)의 이름을 Adjust 토큰으로 옮기는 표. " +
                 "표에 없는 이름은 전송하지 않는다 — Adjust는 이름이 아니라 토큰만 받기 때문이다.")]
        [SerializeField] private List<EventTokenEntry> _eventTokens = new();

        [Tooltip("LogPurchase가 사용할 토큰. 비워 두면 구매를 Adjust로 보내지 않는다.")]
        [SerializeField] private string _purchaseEventToken;

        [Tooltip("표에 없는 이벤트 이름을 토큰으로 그대로 간주해 전송한다. " +
                 "이벤트 상수를 아예 Adjust 토큰 문자열로 쓰는 프로젝트를 위한 옵션이다.")]
        [SerializeField] private bool _treatUnmappedNamesAsTokens;

        [Header("User")]
        [Tooltip("SetUserId가 실릴 전역 콜백 파라미터의 키. " +
                 "Adjust에는 런타임 SetUserId가 없어(ExternalDeviceId는 초기화 시점 전용) " +
                 "전역 콜백 파라미터로 모든 이벤트에 붙인다.")]
        [SerializeField] private string _userIdCallbackKey = "user_id";

        public AdjustEnvironment Environment =>
            _forceSandboxInDevelopmentBuild && Debug.isDebugBuild
                ? AdjustEnvironment.Sandbox
                : _environment;

        public AdjustLogLevel LogLevel => _logLevel;
        public bool SendInBackground => _sendInBackground;
        public string PurchaseEventToken => _purchaseEventToken;
        public bool TreatUnmappedNamesAsTokens => _treatUnmappedNamesAsTokens;
        public string UserIdCallbackKey => _userIdCallbackKey;

        public IReadOnlyList<EventTokenEntry> EventTokens => _eventTokens;

        // 에디터에서는 UNITY_ANDROID/UNITY_IOS가 빌드 타깃을 따라간다(AdUnitId와 같은 규칙).
        public string AppToken
        {
#if UNITY_IOS
            get => _iosAppToken;
#elif UNITY_ANDROID
            get => _androidAppToken;
#else
            get => string.Empty;
#endif
        }
    }
}
