namespace DarkNaku.FoundationDI.Editor
{
    internal enum AppIdAction
    {
        Skip,
        Inject,
        Fail,
    }

    // 빌드 후처리의 판정만 모은 순수 함수. 파일 I/O와 분리해 EditMode에서 검증한다
    // (IosExportCompliance.ShouldDeclare와 같은 방식).
    internal static class UmpBuildRules
    {
        internal const string AndroidAppIdKey = "com.google.android.gms.ads.APPLICATION_ID";
        internal const string IosAppIdKey = "GADApplicationIdentifier";
        internal const string AndroidFirebaseCollectionKey = "firebase_analytics_collection_enabled";
        internal const string IosFirebaseCollectionKey = "FIREBASE_ANALYTICS_COLLECTION_ENABLED";

        internal static AppIdAction DecideAppId(bool useUmp, string configuredId, bool alreadyPresent)
        {
            if (!useUmp || alreadyPresent) return AppIdAction.Skip;

            return string.IsNullOrEmpty(configuredId) ? AppIdAction.Fail : AppIdAction.Inject;
        }

        // Basic 모드: 동의 전에는 Firebase 자동 이벤트(first_open 등)도 나가면 안 된다.
        // AnalyticsService가 동의 후 SetCollectionEnabled(true)로 켠다.
        internal static bool ShouldDisableFirebaseCollection(bool waitForConsent, bool firebaseActive, bool alreadyPresent)
        {
            return waitForConsent && firebaseActive && !alreadyPresent;
        }
    }
}
