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

        // 이 provider에는 provider-전역 임프레션 경로가 없다 — 더미 임프레션은 전부
        // 어댑터별 Paid로 온다. 여기서 진짜로 전달하면 AdService.BuildAdUnits가 어댑터 Paid와
        // 이 이벤트를 둘 다 구독해 같은 임프레션이 두 번 집계된다(Task 9에서 고친 결함과 동일 패턴).
        // 그래서 인터페이스 계약은 지키되 아무 일도 하지 않는 no-op으로 둔다.
        public event Action<AdImpression> ImpressionPaid { add { } remove { } }

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
            new DummyFullScreenAdapter(AdFormat.Interstitial, adUnitId, _dispatcher, _screen, _options, _random);

        public IFullScreenAdapter CreateRewarded(string adUnitId) =>
            new DummyFullScreenAdapter(AdFormat.Rewarded, adUnitId, _dispatcher, _screen, _options, _random);

        public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options) =>
            new DummyBannerAdapter(_screen, adUnitId, options, _options);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 외부에서 받은 화면은 소유권이 없으므로 해제하지 않는다.
            if (_ownsScreen) _screen?.Dispose();
            _screen = null;
        }
    }
}
