using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 에디터, UMP를 끈 프로젝트, 네이티브 SDK가 빠진 빌드에서 쓴다. "규제 대상 아님"처럼 동작해
    // 광고·분석을 막지 않는다.
    public sealed class UnsupportedUmpConsent : IUmpConsent
    {
        public bool IsSupported => false;
        public UmpConsentStatus Status => UmpConsentStatus.NotRequired;
        public bool CanRequestAds => true;
        public bool IsPrivacyOptionsRequired => false;

        public TcfSignals ReadTcf() => TcfSignals.None;

        public Awaitable<bool> UpdateAsync(UmpRequestOptions options) => Completed(true);
        public Awaitable<bool> LoadAndShowIfRequiredAsync() => Completed(true);
        public Awaitable<bool> ShowPrivacyOptionsAsync() => Completed(true);

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
