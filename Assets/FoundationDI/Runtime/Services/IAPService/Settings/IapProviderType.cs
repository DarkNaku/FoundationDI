namespace DarkNaku.FoundationDI
{
    // AdService의 AdProviderType과 달리 [Flags]가 아니다 — 스토어는 플랫폼당 하나뿐이라
    // 두 provider가 동시에 붙을 일이 없다.
    public enum IapProviderType
    {
        Dummy,
        UnityIAP,
    }
}
