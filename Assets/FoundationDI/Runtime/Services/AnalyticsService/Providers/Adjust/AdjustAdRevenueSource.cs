using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AdImpression.AdPlatform("AdMob"/"LevelPlay"/"AppLovin")을 Adjust가 정한 광고 수익 소스
    // 문자열로 옮긴다. Adjust는 이 문자열로 어느 미디에이션에서 온 수익인지 구분하므로
    // 임의의 값을 넣으면 대시보드에서 집계가 갈라진다.
    //
    // 설정으로 빼지 않은 이유: 값이 Adjust가 문서로 고정한 상수 집합이고, AdPlatform 쪽도
    // AdService의 어댑터가 채우는 닫힌 집합이다. 양쪽 다 우리가 아는 값이라 인스펙터에서
    // 바꿀 여지를 주면 오타로 집계를 깨뜨릴 자리만 생긴다.
    internal static class AdjustAdRevenueSource
    {
        // Adjust 문서의 소스 상수. 모르는 미디에이션에서 온 수익은 publisher_sdk로 보낸다
        // (Adjust가 "그 외" 용도로 지정한 값이다).
        public const string Fallback = "publisher_sdk";

        private static readonly Dictionary<string, string> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "AppLovin", "applovin_max_sdk" },
                { "AppLovinMAX", "applovin_max_sdk" },
                { "LevelPlay", "ironsource_sdk" },
                { "IronSource", "ironsource_sdk" },
                { "AdMob", "admob_sdk" },
                { "UnityAds", "unity_sdk" },
                { "TopOn", "topon_sdk" },
                { "TradPlus", "tradplus_sdk" },
                { "Admost", "admost_sdk" },
            };

        private static readonly HashSet<string> _warned = new();

        public static string Resolve(string adPlatform)
        {
            if (string.IsNullOrEmpty(adPlatform)) return Fallback;

            if (_map.TryGetValue(adPlatform, out var source)) return source;

            WarnOnce(adPlatform);
            return Fallback;
        }

        private static void WarnOnce(string adPlatform)
        {
            if (!_warned.Add(adPlatform)) return;

            Debug.LogWarning($"[Analytics/Adjust] 광고 플랫폼 '{adPlatform}' 에 대응하는 Adjust 수익 소스를 모른다. " +
                             $"'{Fallback}' 으로 보낸다.");
        }
    }
}
