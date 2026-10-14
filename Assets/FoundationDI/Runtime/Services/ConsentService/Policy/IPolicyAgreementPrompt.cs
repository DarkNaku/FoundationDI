using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 수락 창. 기본 구현(DefaultPolicyAgreementPrompt)을 쓰거나 게임이 직접 구현해
    // RegisterConsentService(settings, prompt:)로 넘긴다.
    //
    // 구현은 "보여 주고, 링크를 열고, 동의 버튼이 눌리면 완료"만 한다. 수락 저장과 버전 비교는
    // ConsentService가 맡는다. 거절 경로는 없다 — 수락해야 게임이 진행된다.
    public interface IPolicyAgreementPrompt
    {
        // 사용자가 동의 버튼을 누르면 완료된다. 표시할 수 없으면 예외를 던진다(조용히 완료하면
        // 사용자가 보지도 않은 약관을 수락한 것으로 저장된다).
        Awaitable ShowAsync(PolicyAgreementContent content);

        // 표시 중이면 창을 정리한다. ShowAsync는 완료시키지 않는다. ConsentService가 Dispose될 때 부른다.
        void Close();
    }
}
