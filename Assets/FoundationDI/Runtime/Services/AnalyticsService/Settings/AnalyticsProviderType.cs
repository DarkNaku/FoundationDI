using System;

namespace DarkNaku.FoundationDI
{
    // AdService의 AdProviderType과 달리 [Flags]다. 광고는 미디에이션 SDK 하나만 붙지만,
    // 분석은 Firebase + MMP 하나 이상이 동시에 붙는 것이 정상이기 때문이다.
    [Flags]
    public enum AnalyticsProviderType
    {
        None = 0,
        Debug = 1 << 0,
        Firebase = 1 << 1,
        AppsFlyer = 1 << 2,
        Adjust = 1 << 3,
        Singular = 1 << 4,
        Airbridge = 1 << 5,
    }
}
