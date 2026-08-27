using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    public interface IAnalyticsProviderFactory
    {
        // providerSettings는 어댑터 고유 설정 목록이다. 코어는 내용을 모른 채 creator에게
        // 그대로 넘기기만 한다 — 자기 것을 고르는 일은 어댑터가 타입으로 한다.
        IReadOnlyList<IAnalyticsProvider> CreateAll(
            AnalyticsProviderType types,
            AnalyticsServiceOptions options,
            IReadOnlyList<AnalyticsProviderSettings> providerSettings = null);
    }
}
