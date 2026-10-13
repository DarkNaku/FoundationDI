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

        // 앱 ID가 "이미 있는가"는 소스 매니페스트(src/main, *.androidlib)로만 판단한다.
        // build/ 아래에는 이전 Gradle 내보내기의 병합 산출물이 남아 있어, 거기 남은 ID를 근거로 건너뛰면
        // 소스에서 ID가 빠졌을 때 빌드는 통과하고 앱이 실행 시점에 크래시한다.
        internal static bool IsSourceManifest(string path)
        {
            var normalized = path.Replace('\\', '/');

            if (normalized.Contains("/build/")) return false;

            return normalized.EndsWith("/src/main/AndroidManifest.xml") ||
                   normalized.Contains(".androidlib/");
        }

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
