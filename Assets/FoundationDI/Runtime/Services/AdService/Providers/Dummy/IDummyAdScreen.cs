using System;

namespace DarkNaku.FoundationDI
{
    // 가짜 광고 화면 seam. 어댑터의 지연/실패/보상 로직을 uGUI 없이 테스트하기 위해 분리한다.
    public interface IDummyAdScreen : IDisposable
    {
        // onComplete는 카운트다운 완주, onSkip은 중간 닫기. 둘 중 하나만 호출된다.
        void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete);

        void ShowBanner(BannerPosition position, float height);
        void HideBanner();
    }
}
