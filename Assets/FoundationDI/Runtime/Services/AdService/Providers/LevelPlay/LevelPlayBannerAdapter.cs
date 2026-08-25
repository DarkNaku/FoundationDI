using System;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 배너 광고 단위 하나를 나타내는 얇은 LevelPlay 래퍼.
    //
    // BannerAdUnit(정책 계층)의 EnsureAdapter()는 어댑터가 없을 때만(최초 Show(), 또는
    // Destroy() 이후 다시 Show()할 때) 새로 만들고, 그 사이의 Hide()/Show() 반복에서는 기존
    // 인스턴스를 재사용한다(Ads/BannerAdUnit.cs). 그래서 SDK 배너 객체 생성과 LoadAd()는
    // 생성자에서 한 번만 하고, Show()/Hide()는 이미 만들어진 배너를 보이고 감추기만 한다.
    public class LevelPlayBannerAdapter : IBannerAdapter
    {
        private readonly ILevelPlayBannerAd _bannerAd;
        private readonly IAdDispatcher _dispatcher;

        // OnAdImpressionDataReady는 메인 스레드 보장이 없다(LevelPlayFullScreenAdapter의
        // 같은 핸들러 주석 참고). 그 핸들러가 이 플래그를 읽으므로 volatile이어야 한다.
        private volatile bool _isDisposed;

        private float _height;

        public float Height => _height;

        public event Action<float> HeightChanged;
        public event Action<AdImpression> Paid;

        public LevelPlayBannerAdapter(string adUnitId, BannerOptions options, IAdDispatcher dispatcher)
        {
            _dispatcher = dispatcher;

            // SetDisplayOnLoad(false)가 핵심이다. 기본값(true)이면 로드가 끝나는 즉시 배너가
            // 화면에 뜨는데, 표시 시점을 정하는 것은 BannerAdUnit(정책 계층)이다 —
            // AdsRemoved 상태에서 배너가 잠깐이라도 노출되면 임프레션이 발생해 수익 리포트가
            // 오염된다.
            var config = new LevelPlayBannerAd.Config.Builder()
                .SetSize(options.ToLevelPlayAdSize())
                .SetPosition(options.Position.ToLevelPlayPosition())
                .SetDisplayOnLoad(false)
                .Build();

            _bannerAd = new LevelPlayBannerAd(adUnitId, config);

            // 배너는 갱신을 SDK가 자체 처리하므로(Ads/BannerAdUnit.cs 주석 참고) OnAdLoaded를
            // 로드 알림이 아니라 "레이아웃이 확정됐으니 높이를 다시 읽어라"는 신호로만 쓴다.
            _bannerAd.OnAdLoaded += OnAdLoaded;
            _bannerAd.OnAdLoadFailed += OnAdLoadFailed;
            _bannerAd.OnAdImpressionDataReady += OnImpressionDataReady;

            _bannerAd.LoadAd();
        }

        public void Show()
        {
            if (_isDisposed) return;

            _bannerAd.ShowAd();

            // BannerAdUnit.Hide()는 HeightChanged(0)을 쏘고 내려간다(Ads/BannerAdUnit.cs).
            // 그 뒤 다시 Show()해도 SDK가 다음 자동 갱신(OnAdLoaded)을 낼 때까지는 아무
            // 이벤트도 오지 않아, 이벤트로 레이아웃을 잡는 소비자가 배너가 실제로 보이는
            // 동안에도 높이 0으로 남는다. 표시 시점에 현재 크기를 다시 읽어 즉시 알려준다 —
            // 값이 이전과 같아도 소비자 입장에선 0에서 실제값으로 "변경"된 것이므로 항상 쏜다.
            UpdateHeight(_bannerAd.GetAdSize(), alwaysNotify: true);
        }

        public void Hide()
        {
            if (_isDisposed) return;

            // HeightChanged(0)은 BannerAdUnit.Hide()가 이미 쏜다. 여기서 또 쏘면 중복이다.
            _bannerAd.HideAd();
        }

        // 최초 로드와 SDK 자동 갱신 때마다 온다. 어댑티브 배너는 화면 폭/방향에 따라 높이가
        // 바뀔 수 있으므로 매번 다시 읽는다. AdInfo가 실은 크기를 실어 오지만
        // (LevelPlayAdInfo.AdSize) 파싱에 실패하면 null이므로(LevelPlayAdInfo.GetAdSize의
        // catch 분기) 그때는 배너 객체에 직접 묻는다.
        private void OnAdLoaded(LevelPlayAdInfo info)
        {
            if (_isDisposed) return;

            UpdateHeight(info?.AdSize ?? _bannerAd.GetAdSize(), alwaysNotify: false);
        }

        private void UpdateHeight(LevelPlayAdSize size, bool alwaysNotify)
        {
            // 크기를 알 수 없으면 이전 값을 유지한다. 0으로 덮으면 배너가 떠 있는데도
            // 레이아웃이 배너 없는 상태로 되돌아간다.
            if (size == null) return;

            var height = LevelPlayAdMapper.DpToPixels(size.Height);

            if (!alwaysNotify && Mathf.Approximately(height, _height)) return;

            _height = height;
            HeightChanged?.Invoke(_height);
        }

        // IBannerAdapter에는 로드 실패를 알릴 이벤트가 없다(배너는 재시도 개념이 없다 —
        // SDK가 자체 갱신하므로 정책 계층도 이걸 모른다). 그래도 완전히 삼키면 "배너가 그냥
        // 안 뜬다"만 남고 원인을 알 방법이 없어 로그로 남긴다.
        private void OnAdLoadFailed(LevelPlayAdError error)
        {
            if (_isDisposed) return;

            Debug.LogWarning($"[AdService] LevelPlay 배너 로드 실패: {error.ToAdError()}");
        }

        // 메인 스레드 보장이 없다. 근거와 마샬링 이유는 LevelPlayFullScreenAdapter의 같은
        // 핸들러 주석에 적어 두었다(배너도 동일하다 —
        // Runtime/Platforms/Android/UnityLevelPlayImpressionDataListener.cs 경로를 공유한다).
        private void OnImpressionDataReady(LevelPlayImpressionData data)
        {
            if (_isDisposed || data == null) return;

            var revenue = data.Revenue;
            if (!revenue.HasValue || revenue.Value <= 0d) return;

            var impression = data.ToAdImpression(AdFormat.Banner);

            _dispatcher.Post(() =>
            {
                if (_isDisposed) return;
                Paid?.Invoke(impression);
            });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _bannerAd.OnAdLoaded -= OnAdLoaded;
            _bannerAd.OnAdLoadFailed -= OnAdLoadFailed;
            _bannerAd.OnAdImpressionDataReady -= OnImpressionDataReady;

            _bannerAd.DestroyAd();

            _height = 0f;
            HeightChanged = null;
            Paid = null;
        }
    }
}
