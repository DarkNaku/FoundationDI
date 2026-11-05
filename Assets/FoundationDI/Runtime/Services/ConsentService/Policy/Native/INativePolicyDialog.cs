using System;

namespace DarkNaku.FoundationDI
{
    // OS 네이티브 약관 창. 플랫폼 구현(iOS 커스텀 알림창, Android AlertDialog)과
    // NativePolicyAgreementPrompt 사이의 seam이다.
    public interface INativePolicyDialog
    {
        // 이 기기에서 띄울 수 있는가. false면 NativePolicyAgreementPrompt가 대체 창을 쓴다.
        bool IsSupported { get; }

        // OK가 눌리면 onAccepted를 메인 스레드에서 부른다. 창은 OK 말고는 닫히지 않는다.
        void Show(PolicyAgreementContent content, PolicyMessage message, Action onAccepted);

        // 콜백 없이 창을 내린다.
        void Close();
    }
}
