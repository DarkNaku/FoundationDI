using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 배너 광고 단위 하나를 나타내는 얇은 MAX 래퍼. BannerAdUnit(정책 계층)이 Show()를 부를
    // 때마다 이 어댑터를 새로 만들므로(팩토리 패턴, Ads/BannerAdUnit.cs 참고), 생성자에서
    // 곧바로 CreateBanner를 건다.
    public class AppLovinBannerAdapter : IBannerAdapter
    {
        private readonly string _adUnitId;

        private bool _isDisposed;
        private float _height;

        public float Height => _height;

        public event Action<float> HeightChanged;
        public event Action<AdImpression> Paid;

        public AppLovinBannerAdapter(string adUnitId, BannerOptions options)
        {
            _adUnitId = adUnitId;

            // 배너는 갱신을 SDK가 자체 처리하므로(Ads/BannerAdUnit.cs 주석 참고) Loaded 콜백을
            // 로드 알림이 아니라 "레이아웃이 확정됐으니 높이를 다시 읽어라"는 신호로만 쓴다.
            MaxSdkCallbacks.Banner.OnAdLoadedEvent += OnAdLoaded;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnAdRevenuePaid;

            // BannerOptions.Position은 상/하 두 값뿐이라 MAX의 9방향 AdViewPosition 중
            // 중앙 정렬 두 값으로만 매핑한다. BannerOptions.Size는 매핑하지 않는다 — MAX는
            // CreateBanner에 사이즈를 넘기는 API가 없다. Banner/MREC/Leader 중 어느
            // 포맷인지는 AppLovin 대시보드에서 그 ad unit id를 만들 때 이미 고정되고,
            // 남은 자유도는 IsAdaptive 하나뿐이다.
            var position = options.Position == BannerPosition.Top
                ? MaxSdkBase.AdViewPosition.TopCenter
                : MaxSdkBase.AdViewPosition.BottomCenter;

            var configuration = new MaxSdkBase.AdViewConfiguration(position) { IsAdaptive = options.UseAdaptive };

            MaxSdk.CreateBanner(_adUnitId, configuration);

            // MAX 요구사항: 배너 배경은 불투명이어야 한다.
            MaxSdk.SetBannerBackgroundColor(_adUnitId, Color.black);

            // CreateBanner는 생성과 동시에 표시까지 한다. 표시 시점은 BannerAdUnit(정책
            // 계층)이 결정해야 하므로 만들자마자 숨긴다.
            MaxSdk.HideBanner(_adUnitId);
        }

        public void Show()
        {
            if (_isDisposed) return;
            MaxSdk.ShowBanner(_adUnitId);
        }

        public void Hide()
        {
            if (_isDisposed) return;
            MaxSdk.HideBanner(_adUnitId);
        }

        // 로드(최초 및 자동 갱신) 때마다 실제 온스크린 레이아웃을 다시 읽는다 — 어댑티브
        // 배너는 폭/방향에 따라 높이가 바뀔 수 있고, AdInfo 자체에는 크기 필드가 없다
        // (MaxSdkUtils.GetAdaptiveBannerHeight는 AdMob/GAM 전용이라 MAX 자체 배너에는
        // 쓸 수 없다 — Assets/MaxSdk/Scripts/MaxSdkUtils.cs 주석 확인).
        private void OnAdLoaded(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;

            var layout = MaxSdk.GetBannerLayout(_adUnitId);
            if (Mathf.Approximately(layout.height, _height)) return;

            _height = layout.height;
            HeightChanged?.Invoke(_height);
        }

        private void OnAdRevenuePaid(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Paid?.Invoke(info.ToAdImpression(AdFormat.Banner));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent -= OnAdLoaded;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent -= OnAdRevenuePaid;

            MaxSdk.DestroyBanner(_adUnitId);

            _height = 0f;
            HeightChanged = null;
            Paid = null;
        }
    }
}
