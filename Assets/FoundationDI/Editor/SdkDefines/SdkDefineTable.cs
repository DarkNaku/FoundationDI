using System.Collections.Generic;

namespace DarkNaku.FoundationDI.Editor
{
    // 자동 관리 대상 SDK 하나. SDK를 추가하려면 아래 Entries에 한 줄 넣는 것이 전부다.
    public readonly struct SdkDefineEntry
    {
        // 스크립팅 심볼. 어댑터 asmdef의 defineConstraints와 같아야 한다.
        public string Symbol { get; }

        // SDK가 프로젝트에 있는지 판정할 어셈블리 이름.
        //
        // 어댑터 asmdef가 참조하는 그 어셈블리를 그대로 적는다 — precompiled DLL이면
        // DLL 이름(Firebase), asmdef면 asmdef 이름(Unity.Purchasing, MaxSdk.Scripts).
        // 어댑터가 컴파일되기 위해 필요한 것과 판정 기준이 같아야 둘이 어긋나지 않는다.
        //
        // 폴더 경로로 판정하지 않는 이유는 두 가지다. 사용자가 SDK 폴더를 옮길 수 있고,
        // UPM으로 오는 SDK(Unity IAP)는 Assets/ 밖에 있어 경로로는 아예 찾을 수 없다.
        public string AssemblyName { get; }

        // 로그와 메뉴에 보여줄 이름.
        public string DisplayName { get; }

        public SdkDefineEntry(string symbol, string assemblyName, string displayName)
        {
            Symbol = symbol;
            AssemblyName = assemblyName;
            DisplayName = displayName;
        }
    }

    public static class SdkDefineTable
    {
        // FOUNDATIONDI_ADMOB는 일부러 빠져 있다.
        // 이 심볼은 어댑터 어셈블리가 아직 없어서, 켜면 AdProviderFactory.IsAvailable이
        // true가 되고 곧바로 "creator가 없다"는 에러 로그로 이어진다.
        // 어댑터가 생기는 시점에 여기에 한 줄 추가한다.
        public static readonly IReadOnlyList<SdkDefineEntry> Entries = new[]
        {
            new SdkDefineEntry("FOUNDATIONDI_FIREBASE",
                               "Firebase.Analytics",
                               "Firebase Analytics"),

            new SdkDefineEntry("FOUNDATIONDI_UNITYIAP",
                               "Unity.Purchasing",
                               "Unity In-App Purchasing"),

            new SdkDefineEntry("FOUNDATIONDI_APPLOVIN",
                               "MaxSdk.Scripts",
                               "AppLovin MAX"),

            new SdkDefineEntry("FOUNDATIONDI_LEVELPLAY",
                               "Unity.LevelPlay",
                               "Unity LevelPlay"),
        };
    }
}
