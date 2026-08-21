using System;

namespace DarkNaku.FoundationDI
{
    public class DummyBannerAdapter : IBannerAdapter
    {
        private readonly IDummyAdScreen _screen;
        private readonly string _adUnitId;
        private readonly BannerOptions _bannerOptions;
        private readonly DummyAdOptions _options;
        private bool _isDisposed;

        public float Height { get; private set; }

        public event Action<float> HeightChanged;
        public event Action<AdImpression> Paid;

        public DummyBannerAdapter(IDummyAdScreen screen, string adUnitId, BannerOptions bannerOptions,
                                  DummyAdOptions options)
        {
            _screen = screen;
            _adUnitId = adUnitId;
            _bannerOptions = bannerOptions;
            _options = options;
        }

        public void Show()
        {
            if (_isDisposed) return;

            _screen.ShowBanner(_bannerOptions.Position, _options.BannerHeight);

            Height = _options.BannerHeight;
            HeightChanged?.Invoke(Height);

            Paid?.Invoke(new AdImpression(AdFormat.Banner, "Dummy", "DummyNetwork", _adUnitId,
                                          "dummy-instance", null, 0.002, "USD",
                                          AdRevenuePrecision.Estimated, "dummy-creative"));
        }

        public void Hide()
        {
            if (_isDisposed) return;

            _screen.HideBanner();
            Height = 0f;
            HeightChanged?.Invoke(0f);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _screen.HideBanner();
            Height = 0f;
        }
    }
}
