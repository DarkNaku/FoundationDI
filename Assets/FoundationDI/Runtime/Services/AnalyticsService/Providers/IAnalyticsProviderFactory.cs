using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    public interface IAnalyticsProviderFactory
    {
        IReadOnlyList<IAnalyticsProvider> CreateAll(AnalyticsProviderType types,
                                                    AnalyticsServiceOptions options);
    }
}
