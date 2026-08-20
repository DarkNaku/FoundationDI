using System;

namespace DarkNaku.FoundationDI
{
    // 배너 정책 계층. 전면/보상과 달리 재시도·자동 재로드를 하지 않는다 —
    // 세 SDK 모두 배너 갱신을 SDK가 처리하고, 배너는 화면에 계속 두는 것이 권장된다.
    public class BannerAdUnit : IBannerAd, IDisposable
    {
        private readonly Func<IBannerAdapter> _adapterFactory;
        private readonly Func<bool> _adsRemoved;

        private IBannerAdapter _adapter;
        private bool _wantsVisible;
        private bool _isDisposed;

        public event Action<float> HeightChanged;
        public event Action<AdImpression> Paid;

        public BannerAdUnit(Func<IBannerAdapter> adapterFactory, Func<bool> adsRemoved)
        {
            _adapterFactory = adapterFactory;
            _adsRemoved = adsRemoved ?? (() => false);
        }

        // 광고가 제거됐으면 보이지 않는 것으로 취급한다 — 호출자가 두 조건을 따로 볼 필요가 없다.
        public bool IsVisible => !_isDisposed && _wantsVisible && !_adsRemoved();

        public float Height => IsVisible && _adapter != null ? _adapter.Height : 0f;

        public void Show()
        {
            if (_isDisposed) return;

            _wantsVisible = true;

            // 광고제거 상태에서는 어댑터를 만들지도 않는다. SDK가 배너를 요청하면
            // 임프레션이 발생하고 수익 리포트가 오염된다.
            if (_adsRemoved()) return;

            EnsureAdapter();
            _adapter.Show();
        }

        public void Hide()
        {
            _wantsVisible = false;
            _adapter?.Hide();
            HeightChanged?.Invoke(0f);
        }

        public void Destroy()
        {
            _wantsVisible = false;
            DetachAdapter();
            HeightChanged?.Invoke(0f);
        }

        // AdService가 AdsRemoved 변경 시 호출한다.
        public void OnAdsRemovedChanged(bool removed)
        {
            if (removed) { DetachAdapter(); HeightChanged?.Invoke(0f); }
            else if (_wantsVisible) Show();
        }

        private void EnsureAdapter()
        {
            if (_adapter != null) return;

            _adapter = _adapterFactory();
            _adapter.HeightChanged += OnAdapterHeightChanged;
            _adapter.Paid += OnAdapterPaid;
        }

        private void DetachAdapter()
        {
            if (_adapter == null) return;

            _adapter.HeightChanged -= OnAdapterHeightChanged;
            _adapter.Paid -= OnAdapterPaid;
            _adapter.Dispose();
            _adapter = null;
        }

        private void OnAdapterHeightChanged(float height) => HeightChanged?.Invoke(IsVisible ? height : 0f);
        private void OnAdapterPaid(AdImpression impression) => Paid?.Invoke(impression);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _wantsVisible = false;
            DetachAdapter();
        }
    }
}
