using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Google UMP 네이티브 SDK를 가리는 seam. GMA Unity 플러그인을 쓰지 않는다 — 그 플러그인은
    // 광고 SDK를 함께 끌고 와 LevelPlay AdMob 어댑터(Next-Gen SDK)와 충돌한다(스펙 §1.2).
    //
    // 계약: 모든 Awaitable은 메인 스레드에서 완료된다(구현이 마샬링한다). 영원히 매달리는 것은
    // 구현의 버그지만, ConsentService는 네트워크 단계(UpdateAsync)에 타임아웃을 따로 건다.
    public interface IUmpConsent
    {
        // 네이티브 SDK가 실제로 있는가. false면 ConsentService는 UMP를 쓰지 않는 것으로 본다.
        bool IsSupported { get; }

        UmpConsentStatus Status { get; }
        bool CanRequestAds { get; }
        bool IsPrivacyOptionsRequired { get; }

        TcfSignals ReadTcf();

        // requestConsentInfoUpdate. 성공이면 true, 실패(네트워크 등)면 false.
        Awaitable<bool> UpdateAsync(UmpRequestOptions options);

        // loadAndShowConsentFormIfRequired. 필요 없으면 폼 없이 즉시 끝난다. 에러가 없으면 true.
        Awaitable<bool> LoadAndShowIfRequiredAsync();

        // showPrivacyOptionsForm. 에러가 없으면 true.
        Awaitable<bool> ShowPrivacyOptionsAsync();
    }
}
