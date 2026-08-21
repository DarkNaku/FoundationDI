using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 동의 개념이 없는 provider(Dummy 등)의 기본 구현. 항상 요청 가능으로 답한다.
    public class NoopAdConsent : IAdConsent
    {
        public bool CanRequestAds => true;
        public bool IsPrivacyOptionsRequired => false;

        public Awaitable<bool> RequestAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public Awaitable ShowPrivacyOptionsAsync()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
