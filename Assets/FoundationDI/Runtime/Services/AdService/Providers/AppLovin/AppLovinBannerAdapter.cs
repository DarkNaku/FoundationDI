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
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += OnAdLoadFailed;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnAdRevenuePaid;

            // BannerOptions.Position은 상/하 두 값뿐이라 MAX의 9방향 AdViewPosition 중
            // 중앙 정렬 두 값으로만 매핑한다.
            var position = options.Position == BannerPosition.Top
                ? MaxSdkBase.AdViewPosition.TopCenter
                : MaxSdkBase.AdViewPosition.BottomCenter;

            var configuration = new MaxSdkBase.AdViewConfiguration(position) { IsAdaptive = options.UseAdaptive };

            // BannerOptions.Size 중 Standard/Adaptive를 넘는 값(Large/MediumRectangle/
            // Leaderboard)은 아직 구현하지 않았다 — MAX 자체가 지원하지 않는 게 아니라
            // (MaxSdk.CreateMRec(string, AdViewConfiguration)로 MediumRectangle은 실제로
            // 낼 수 있다), 이 어댑터가 그 경로를 아직 다루지 않는다는 뜻이다. MREC은
            // CreateBanner가 아니라 별도의 ad unit 타입/전용 API(CreateMRec/ShowMRec/
            // DestroyMRec)라 지금 있는 IBannerAdapter 하나로 두 포맷을 겸용할 수 없고,
            // 조용히 무시하면 "설정했는데 왜 MREC 크기로 안 뜨지"를 디버깅할 단서가 없어진다.
            if (options.Size != BannerSize.Standard && options.Size != BannerSize.Adaptive)
            {
                Debug.LogWarning($"[AdService] AppLovin 배너 어댑터는 BannerOptions.Size={options.Size}를 " +
                                 "구현하지 않았다 — 표준/적응형 배너로만 표시된다. 이 크기가 필요하면 " +
                                 "MaxSdk.CreateMRec(전용 MREC ad unit)을 쓰는 별도 어댑터/경로가 필요하다.");
            }

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

            // BannerAdUnit.Hide()는 HeightChanged(0)을 쏘고 내려간다(Ads/BannerAdUnit.cs).
            // 그 뒤 다시 Show()해도 SDK가 다음 자동 갱신(OnAdLoadedEvent)을 낼 때까지는
            // 아무 이벤트도 오지 않아, 이벤트 기반으로 레이아웃을 잡는 소비자가 광고가 실제로
            // 다시 보이는 동안에도 높이 0으로 남는다. 표시 시점에 실제 레이아웃을 다시 읽어
            // 즉시 알려준다 — 값이 이전과 같아도(Hide 전과 같은 배너라면 보통 같다) 소비자
            // 입장에선 0에서 실제값으로 "변경"된 것이므로 항상 발화한다.
            var layout = MaxSdk.GetBannerLayout(_adUnitId);
            _height = layout.height;
            HeightChanged?.Invoke(_height);
        }

        public void Hide()
        {
            if (_isDisposed) return;
            MaxSdk.HideBanner(_adUnitId);
        }

        // 로드(최초 및 자동 갱신) 때마다 실제 온스크린 레이아웃을 다시 읽는다 — 어댑티브
        // 배너는 폭/방향에 따라 높이가 바뀔 수 있고, AdInfo 자체에는 크기 필드가 없다
        // (MaxSdkUtils.GetAdaptiveBannerHeight는 AdMob/GAM 전용이라 MAX 자체 배너에는
        // 쓸 수 없다 — Assets/MaxSdk/Scripts/MaxSdkUtils.cs 주석 확인). 참고: Unity 에디터
        // 스텁(MaxSdkUnityEditor.GetBannerLayout)은 항상 Rect.zero를 돌려주므로, 에디터에서는
        // 이 경로가 실제 높이를 절대 보고하지 않는다 — 디바이스에서만 검증 가능하다.
        private void OnAdLoaded(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;

            var layout = MaxSdk.GetBannerLayout(_adUnitId);
            if (Mathf.Approximately(layout.height, _height)) return;

            _height = layout.height;
            HeightChanged?.Invoke(_height);
        }

        // IBannerAdapter에는 배너 로드 실패를 알릴 이벤트가 없다(배너는 재시도 개념이
        // 없다 — SDK가 자체 갱신하므로 정책 계층도 이걸 모른다). 그래도 완전히 삼키면
        // "배너가 그냥 안 뜬다"만 남고 원인을 알 방법이 없어 로그로만 남긴다.
        private void OnAdLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo error)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;
            Debug.LogWarning($"[AdService] AppLovin 배너 로드 실패 ({error.Code}): {error.Message}");
        }

        private void OnAdRevenuePaid(string adUnitId, MaxSdkBase.AdInfo info)
        {
            if (_isDisposed || adUnitId != _adUnitId) return;

            // -1은 MAX가 "수익 없음"에 채우는 센티널이다(MaxSdkBase.cs:446). 그대로
            // 흘려보내면 임프레션을 합산하는 소비자가 음수/0 매출을 계상한다.
            if (info == null || info.Revenue <= 0d) return;

            Paid?.Invoke(info.ToAdImpression(AdFormat.Banner));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent -= OnAdLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent -= OnAdLoadFailed;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent -= OnAdRevenuePaid;

            MaxSdk.DestroyBanner(_adUnitId);

            _height = 0f;
            HeightChanged = null;
            Paid = null;
        }
    }
}
