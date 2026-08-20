using System;

namespace DarkNaku.FoundationDI
{
    public interface IBannerAd
    {
        bool IsVisible { get; }

        // 화면 픽셀 단위 높이. 미표시/미로드면 0. UI 레이아웃이 배너를 피하는 데 쓴다.
        float Height { get; }

        void Show();
        void Hide();

        // 영구 종료가 아니라 리소스 해제다. 이후 Show()는 어댑터를 새로 만들어 다시 붙인다.
        void Destroy();

        event Action<float> HeightChanged;
    }
}
