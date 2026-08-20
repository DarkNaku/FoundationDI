using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AdMob은 UMP, LevelPlay는 SetConsent, MAX는 T&P Flow로 각각 매핑된다.
    public interface IAdConsent
    {
        bool CanRequestAds { get; }
        bool IsPrivacyOptionsRequired { get; }

        // 필요하면 동의 폼을 띄운다. 완료 시 CanRequestAds가 갱신된다.
        Awaitable<bool> RequestAsync();

        Awaitable ShowPrivacyOptionsAsync();
    }
}
