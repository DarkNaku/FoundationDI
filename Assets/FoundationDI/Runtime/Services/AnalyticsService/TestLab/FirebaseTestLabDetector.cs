using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Test Lab 기기는 Settings.System의 "firebase.test.lab"이 "true"다(Firebase 공식 판별법).
    // Android 밖(iOS 앱 심사 기기 포함)에서는 판별할 방법이 없어 항상 false다.
    public sealed class FirebaseTestLabDetector : ITestLabDetector
    {
        private bool? _isTestLab;

        // 기기 설정이라 실행 중에 바뀌지 않는다. 한 번만 읽는다.
        public bool IsTestLab => _isTestLab ??= Read();

        private static bool Read()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var settings = new AndroidJavaClass("android.provider.Settings$System");

                return settings.CallStatic<string>("getString", resolver, "firebase.test.lab") == "true";
            }
            catch (Exception e)
            {
                // 판별 실패로 실제 사용자의 수집을 막지 않는다.
                Debug.LogError($"[AnalyticsService] Test Lab 판별에 실패했다. 일반 기기로 본다: {e}");
                return false;
            }
#else
            return false;
#endif
        }
    }
}
