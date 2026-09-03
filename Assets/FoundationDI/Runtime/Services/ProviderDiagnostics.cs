namespace DarkNaku.FoundationDI
{
    // AdService / AnalyticsService / IAPService가 provider creator를 찾지 못했을 때 내는 문구를
    // 한 곳에서 만든다.
    //
    // 세 서비스는 provider 어댑터를 옵셔널 어셈블리로 분리하고 레지스트리로 조회하는 구조가
    // 같고, 따라서 실패 원인도 같다 — 심볼이 없어 어댑터가 컴파일되지 않았거나, IL2CPP 빌드에서
    // 어댑터 어셈블리가 통째로 스트리핑됐거나. 문구를 각자 들고 있으면 같은 사고가 서비스마다
    // 다른 말로 찍혀 한눈에 같은 문제로 보이지 않는다. 실제로 그래서 한 번 오래 걸렸다.
    internal static class ProviderDiagnostics
    {
        // providerName은 provider enum 이름 그대로다. 어댑터 어셈블리 이름
        // (FoundationDI.AppLovin / .LevelPlay / .Firebase / .Adjust / .UnityIAP)과 심볼
        // (FOUNDATIONDI_*)이 모두 이 이름에서 그대로 유도되므로 별도의 매핑표가 필요 없다.
        public static string MissingCreator(string serviceTag, string providerName, string fallback)
        {
            return $"[{serviceTag}] {providerName} provider가 요청됐지만 등록된 creator가 없다. " +
                   $"FOUNDATIONDI_{providerName.ToUpperInvariant()} 심볼이 없어 어댑터가 컴파일되지 않았거나, " +
                   $"IL2CPP 빌드에서 FoundationDI.{providerName} 어셈블리가 통째로 스트리핑된 것이다" +
                   $"(에디터에서는 재현되지 않는다). {fallback}";
        }
    }
}
