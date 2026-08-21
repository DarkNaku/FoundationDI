using System;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고 단위 하나를 나타내는 얇은 SDK 래퍼.
    // 재시도, 자동 재로드, 보상 확정은 여기 책임이 아니다 — FullScreenAdUnit이 한다.
    // 구현체는 이벤트를 반드시 메인 스레드에서 발화시켜야 한다.
    public interface IFullScreenAdapter : IDisposable
    {
        bool IsReady { get; }
        void Load();
        void Show();

        event Action Loaded;
        event Action<AdError> LoadFailed;
        event Action Displayed;
        event Action<AdError> DisplayFailed;
        event Action Closed;

        // 보상 어댑터에서만 발화한다. 전면 어댑터는 발화시키지 않는다.
        // Closed와의 순서는 SDK/네트워크마다 다르므로 보장하지 않아도 된다.
        event Action<AdReward> Rewarded;

        event Action<AdImpression> Paid;
    }
}
