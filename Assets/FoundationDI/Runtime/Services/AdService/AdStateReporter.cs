using System;

namespace DarkNaku.FoundationDI
{
    public sealed class AdStateReporter : IDisposable
    {
        public const string USER_PROPERTY = "ad_state";

        private readonly IAdService _ads;
        private readonly IAnalyticsService _analytics;

        public AdStateReporter(IAdService ads, IAnalyticsService analytics)
        {
            _ads = ads;
            _analytics = analytics;

            Report(_ads.InitState);
            _ads.InitStateChanged += Report;
        }

        public void Dispose() => _ads.InitStateChanged -= Report;

        private void Report(AdInitState state) => _analytics.SetUserProperty(USER_PROPERTY, ToValue(state));

        private static string ToValue(AdInitState state)
        {
            switch (state)
            {
                case AdInitState.WaitingConsent:
                    return "waiting_consent";
                case AdInitState.Ready:
                    return "ready";
                case AdInitState.ReadyRestricted:
                    return "ready_restricted";
                case AdInitState.Failed:
                    return "failed";
                default:
                    return "pending";
            }
        }
    }
}
