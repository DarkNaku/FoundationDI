using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal static class LevelPlayBannerViewMetrics
    {
        // AndroidJavaObject는 모든 플랫폼에서 컴파일되므로 #if 대신 실행 플랫폼으로 고른다 —
        // 에디터가 어느 빌드 타깃에 있든 Android 구현의 컴파일 에러가 바로 드러난다.
        public static IBannerViewMetrics Create()
        {
            return Application.platform == RuntimePlatform.Android
                ? new AndroidLevelPlayBannerViewMetrics()
                : new ScreenDpiBannerViewMetrics();
        }
    }

    // iOS·에디터. 뷰를 재지 않고 요청 크기를 Screen.dpi로 환산한다(LevelPlayAdMapper.DpToPixels 주석 참고).
    internal sealed class ScreenDpiBannerViewMetrics : IBannerViewMetrics
    {
        public bool CanMeasureView => false;

        public bool TryGetViewHeight(out float pixels)
        {
            pixels = 0f;
            return false;
        }

        public float DpToPixels(float dp) => LevelPlayAdMapper.DpToPixels(dp);
    }

    // LevelPlay Android 브리지(com.ironsource.unity.androidbridge.BannerAd)는 배너 뷰를
    // Activity.addContentView로 android.R.id.content 바로 아래에 붙인다. 그 자식 중
    // LevelPlayBannerAdView(공개 SDK 클래스)를 찾아 실제 높이를 읽는다. 브리지 객체의
    // 내부 필드를 리플렉션으로 꺼내는 것보다 공개 Android API만 써서 덜 깨진다.
    //
    // AdService가 배너를 하나만 띄우므로 보이는 배너 뷰 중 가장 큰 높이를 쓴다.
    internal sealed class AndroidLevelPlayBannerViewMetrics : IBannerViewMetrics
    {
        private const string BANNER_VIEW_CLASS = "com.unity3d.mediation.banner.LevelPlayBannerAdView";
        private const int CONTENT_VIEW_ID = 0x01020002;   // android.R.id.content
        private const int VISIBLE = 0;                     // android.view.View.VISIBLE

        private float? _density;   // 실패하면 0으로 남아 다시 읽지 않는다
        private bool _isViewLookupBroken;

        // 한 번 실패하면 다시 시도하지 않는다. 매 표시마다 같은 예외 로그가 쌓이고
        // 재측정 대기만 늘어난다 — 요청 크기 환산으로 떨어지면 기존 동작과 같다.
        public bool CanMeasureView => !_isViewLookupBroken;

        public bool TryGetViewHeight(out float pixels)
        {
            pixels = 0f;

            if (_isViewLookupBroken) return false;

            try
            {
                using var activity = GetCurrentActivity();
                using var content = activity.Call<AndroidJavaObject>("findViewById", CONTENT_VIEW_ID);

                if (content == null) return false;

                var count = content.Call<int>("getChildCount");

                for (var i = 0; i < count; i++)
                {
                    using var child = content.Call<AndroidJavaObject>("getChildAt", i);

                    if (child == null || !IsVisibleBannerView(child)) continue;

                    pixels = Mathf.Max(pixels, child.Call<int>("getHeight"));
                }

                return pixels > 0f;
            }
            catch (Exception e)
            {
                _isViewLookupBroken = true;
                Debug.LogWarning($"[AdService] LevelPlay 배너 뷰 높이를 읽지 못해 요청 크기로 계산한다: {e.Message}");
                return false;
            }
        }

        // Android의 Screen.dpi는 물리 dpi(xdpi)라 레이아웃이 쓰는 밀도와 다르다.
        // 예: Galaxy A13은 density 2.8125(450dpi)인데 xdpi가 403.4라 50dp 배너가 141px 대신 126px로 나온다.
        public float DpToPixels(float dp)
        {
            _density ??= ReadDensity();

            return _density > 0f ? dp * _density.Value : LevelPlayAdMapper.DpToPixels(dp);
        }

        private static bool IsVisibleBannerView(AndroidJavaObject view)
        {
            using var type = view.Call<AndroidJavaObject>("getClass");

            return type.Call<string>("getName") == BANNER_VIEW_CLASS && view.Call<int>("getVisibility") == VISIBLE;
        }

        private static float ReadDensity()
        {
            try
            {
                using var activity = GetCurrentActivity();
                using var resources = activity.Call<AndroidJavaObject>("getResources");
                using var metrics = resources.Call<AndroidJavaObject>("getDisplayMetrics");

                return metrics.Get<float>("density");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AdService] 화면 밀도를 읽지 못해 Screen.dpi로 계산한다: {e.Message}");
                return 0f;
            }
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");

            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
}
