using System.Collections.Generic;

namespace DarkNaku.FoundationDI.Editor
{
    // 자동 관리 대상 SDK 하나. SDK를 추가하려면 아래 Entries에 한 줄 넣는 것이 전부다.
    public readonly struct SdkDefineEntry
    {
        // 스크립팅 심볼. 어댑터 asmdef의 defineConstraints와 같아야 한다.
        public string Symbol { get; }

        // SDK가 프로젝트에 있는지 판정할 대표 타입의 전체 이름.
        //
        // 폴더 경로로 판정하지 않는 이유가 두 가지다. 사용자가 SDK 폴더를 옮길 수 있고,
        // UPM으로 오는 SDK(Unity IAP)는 Assets/ 밖에 있어 경로로는 아예 찾을 수 없다.
        // 타입 존재는 DLL이든 UPM이든 똑같이 통한다.
        public string MarkerType { get; }

        // 로그와 메뉴에 보여줄 이름.
        public string DisplayName { get; }

        public SdkDefineEntry(string symbol, string markerType, string displayName)
        {
            Symbol = symbol;
            MarkerType = markerType;
            DisplayName = displayName;
        }
    }

    public static class SdkDefineTable
    {
        // FOUNDATIONDI_ADMOB / FOUNDATIONDI_LEVELPLAY는 일부러 빠져 있다.
        // 두 심볼은 어댑터 어셈블리가 아직 없어서, 켜면 AdProviderFactory.IsAvailable이
        // true가 되고 곧바로 "creator가 없다"는 에러 로그로 이어진다.
        // 어댑터가 생기는 시점에 여기에 한 줄 추가한다.
        public static readonly IReadOnlyList<SdkDefineEntry> Entries = new[]
        {
            new SdkDefineEntry("FOUNDATIONDI_FIREBASE",
                               "Firebase.Analytics.FirebaseAnalytics",
                               "Firebase Analytics"),

            new SdkDefineEntry("FOUNDATIONDI_UNITYIAP",
                               "UnityEngine.Purchasing.StoreController",
                               "Unity In-App Purchasing"),

            new SdkDefineEntry("FOUNDATIONDI_APPLOVIN",
                               "MaxSdkBase",
                               "AppLovin MAX"),
        };
    }
}
