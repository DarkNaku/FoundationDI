namespace DarkNaku.FoundationDI
{
    public enum AppUpdateStatus
    {
        // 확인하지 못했다(네트워크 실패·미지원 플랫폼·에디터·스토어 밖 설치). 업데이트 없음과 구분해야
        // 게임이 오프라인일 때 통과시킬지 막을지 고를 수 있다.
        Unknown,
        UpToDate,
        Available,
    }

    public readonly struct AppUpdateCheckResult
    {
        public AppUpdateStatus Status { get; }

        // 스토어에 새 버전이 올라온 뒤 지난 날짜. Android는 기기가 업데이트를 알게 된 뒤 지난 날짜다
        // (clientVersionStalenessDays). 알 수 없으면 null.
        public int? DaysSinceRelease { get; }

        // Play Console에 올릴 때 지정한 업데이트 우선순위(0~5). Android 전용이고 iOS는 null.
        public int? Priority { get; }

        private AppUpdateCheckResult(AppUpdateStatus status, int? daysSinceRelease, int? priority)
        {
            Status = status;
            DaysSinceRelease = daysSinceRelease;
            Priority = priority;
        }

        public static AppUpdateCheckResult Available(int? daysSinceRelease = null, int? priority = null) =>
            new(AppUpdateStatus.Available, daysSinceRelease, priority);

        public static AppUpdateCheckResult UpToDate() => new(AppUpdateStatus.UpToDate, null, null);

        public static AppUpdateCheckResult Unknown() => new(AppUpdateStatus.Unknown, null, null);
    }
}
