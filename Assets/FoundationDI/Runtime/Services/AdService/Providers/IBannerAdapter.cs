using System;

namespace DarkNaku.FoundationDI
{
    // 배너는 갱신을 SDK가 자동 처리하므로 Load/재시도 개념을 노출하지 않는다.
    // 구현체는 이벤트를 반드시 메인 스레드에서 발화시켜야 한다.
    public interface IBannerAdapter : IDisposable
    {
        float Height { get; }   // 화면 픽셀. 미로드/미표시면 0
        void Show();
        void Hide();

        event Action<float> HeightChanged;
        event Action<AdImpression> Paid;
    }
}
