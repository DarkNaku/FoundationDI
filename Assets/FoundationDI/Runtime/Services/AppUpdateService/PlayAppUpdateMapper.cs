namespace DarkNaku.FoundationDI
{
    // Play In-App Updates의 AppUpdateInfo 값을 우리 결과로 번역한다. 네이티브 값은 캐스팅하지 않는다.
    internal static class PlayAppUpdateMapper
    {
        // com.google.android.play.core.install.model.UpdateAvailability
        private const int UPDATE_NOT_AVAILABLE = 1;
        private const int UPDATE_AVAILABLE = 2;
        private const int DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS = 3;

        public static AppUpdateCheckResult ToResult(int updateAvailability, int? stalenessDays, int priority)
        {
            switch (updateAvailability)
            {
                case UPDATE_AVAILABLE:
                    return AppUpdateCheckResult.Available(stalenessDays, priority);
                // 우선순위는 UPDATE_AVAILABLE일 때만 정의된 값이라 싣지 않는다.
                case DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS:
                    return AppUpdateCheckResult.Available(stalenessDays);
                case UPDATE_NOT_AVAILABLE:
                    return AppUpdateCheckResult.UpToDate();
                default:
                    return AppUpdateCheckResult.Unknown();
            }
        }

        public static string StoreUrl(string packageName) => $"market://details?id={packageName}";
    }
}
