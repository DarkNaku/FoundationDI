using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // SDK 없이 전체 흐름을 실기에서 검증하기 위한 provider.
    // 로드 지연과 실패 확률을 설정으로 흉내내므로 재시도·백오프를 눈으로 확인할 수 있다.
    public class DummyAdProvider : IAdProvider
    {
        private readonly IAdDispatcher _dispatcher;
        private readonly DummyAdOptions _options;
        private readonly Func<float> _random;
        private readonly bool _ownsScreen;

        private IDummyAdScreen _screen;
        private bool _isDisposed;

        public string Name => "Dummy";
        public IAdConsent Consent { get; } = new NoopAdConsent();

        // Dummy는 어댑터별 Paid만 쓴다. 전역 경로는 LevelPlay 어댑터를 위한 자리다.
        public event Action<AdImpression> ImpressionPaid;

        public DummyAdProvider(IAdDispatcher dispatcher, DummyAdOptions options,
                               IDummyAdScreen screen = null, Func<float> random = null)
        {
            _dispatcher = dispatcher;
            _options = options;
            _random = random;
            _ownsScreen = screen == null;
            _screen = screen;
        }

        public Awaitable<bool> InitializeAsync(AdProviderContext context)
        {
            _screen ??= new DummyAdCanvas();

            if (context.VerboseLogging) Debug.Log("[AdService] Dummy provider 초기화 완료");

            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public IFullScreenAdapter CreateInterstitial(string adUnitId) =>
            new DummyFullScreenAdapter(AdFormat.Interstitial, _dispatcher, _screen, _options, _random);

        public IFullScreenAdapter CreateRewarded(string adUnitId) =>
            new DummyFullScreenAdapter(AdFormat.Rewarded, _dispatcher, _screen, _options, _random);

        public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options) =>
            new DummyBannerAdapter(_screen, options, _options);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 외부에서 받은 화면은 소유권이 없으므로 해제하지 않는다.
            if (_ownsScreen) _screen?.Dispose();
            _screen = null;
            ImpressionPaid = null;
        }
    }
}
