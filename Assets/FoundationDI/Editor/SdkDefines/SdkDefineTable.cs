using System.Collections.Generic;

namespace DarkNaku.FoundationDI.Editor
{
    // 자동 관리 대상 SDK 하나. SDK를 추가하려면 아래 Entries에 한 줄 넣는 것이 전부다.
    //
    // 이 표는 두 곳이 함께 읽는다.
    //   - SdkDefineSynchronizer: Symbol/AssemblyName/DisplayName으로 스크립팅 심볼을 켜고 끈다.
    //   - FoundationDILinkXmlGenerator: AdapterAssembly/PreservedAssemblies로 IL2CPP 링커에
    //     넘길 link.xml을 만든다.
    //
    // 표를 둘로 나누지 않은 이유는, 둘 다 "우리 옵셔널 어댑터와 그 뒤의 SDK가 무엇인가"라는
    // 같은 사실을 읽기 때문이다. 나누면 어댑터를 추가할 때 한쪽만 갱신되는 실패 모드가 생기고,
    // 그 실패는 에디터에서 아무 증상도 내지 않다가 실기 빌드에서만 드러난다.
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

        // 이 SDK를 감싸는 FoundationDI 옵셔널 어댑터 어셈블리 이름.
        //
        // 코어(FoundationDI)는 이 어셈블리를 참조할 수 없다(순환 참조). 그래서 어댑터는
        // 참조 그래프상 어디서도 닿지 않는 섬이 되고, IL2CPP 링커는 닿지 않는 어셈블리를
        // 통째로 걷어낸다. 그 결과 [RuntimeInitializeOnLoadMethod] 등록 자체가 일어나지
        // 않아 팩토리의 레지스트리 조회가 비고, 서비스가 조용히 Dummy로 떨어진다.
        public string AdapterAssembly { get; }

        // link.xml로 보존해야 하는 서드파티 SDK 어셈블리들.
        //
        // 어댑터만 살리면 부족하다. 링커는 어댑터가 실제로 건드리는 멤버만 남기는데,
        // MAX/LevelPlay/Adjust는 네이티브가 UnitySendMessage로 이름을 찍어 관리 코드를
        // 되부르기 때문에 그 경로를 링커가 볼 수 없다. 그래서 SDK 어셈블리는 통째로 보존한다.
        //
        // 어댑터 asmdef가 참조하는 FoundationDI 외 어셈블리 전부가 여기 있어야 한다
        // (FoundationDILinkTableConsistencyTest가 asmdef와 대조해 강제한다).
        public IReadOnlyList<string> PreservedAssemblies { get; }

        public SdkDefineEntry(string symbol,
                              string assemblyName,
                              string displayName,
                              string adapterAssembly,
                              IReadOnlyList<string> preservedAssemblies)
        {
            Symbol = symbol;
            AssemblyName = assemblyName;
            DisplayName = displayName;
            AdapterAssembly = adapterAssembly;
            PreservedAssemblies = preservedAssemblies ?? new string[0];
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
                               "Firebase Analytics",
                               "FoundationDI.Firebase",
                               new[] { "Firebase.App", "Firebase.Analytics", "Firebase.TaskExtension" }),

            new SdkDefineEntry("FOUNDATIONDI_UNITYIAP",
                               "Unity.Purchasing",
                               "Unity In-App Purchasing",
                               "FoundationDI.UnityIAP",
                               new[] { "Unity.Purchasing", "Unity.Purchasing.Security", "Unity.Purchasing.SecurityCore" }),

            new SdkDefineEntry("FOUNDATIONDI_APPLOVIN",
                               "MaxSdk.Scripts",
                               "AppLovin MAX",
                               "FoundationDI.AppLovin",
                               new[] { "MaxSdk.Scripts" }),

            new SdkDefineEntry("FOUNDATIONDI_LEVELPLAY",
                               "Unity.LevelPlay",
                               "Unity LevelPlay",
                               "FoundationDI.LevelPlay",
                               new[] { "Unity.LevelPlay" }),

            new SdkDefineEntry("FOUNDATIONDI_ADJUST",
                               "AdjustSdk.Scripts",
                               "Adjust",
                               "FoundationDI.Adjust",
                               new[] { "AdjustSdk.Scripts" }),
        };
    }
}
