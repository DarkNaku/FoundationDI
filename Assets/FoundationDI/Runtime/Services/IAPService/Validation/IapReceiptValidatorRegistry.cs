namespace DarkNaku.FoundationDI
{
    // 옵셔널 어셈블리(FoundationDI.UnityIAP)가 자신의 검증기를 밀어 넣는 슬롯이다.
    // 코어는 UnityEngine.Purchasing.Security를 참조할 수 없으므로 직접 만들 수 없다.
    //
    // 레지스트리가 Dictionary가 아니라 슬롯 하나인 이유: 검증기는 provider와 달리
    // "어느 SDK를 쓰는가"와 1:1이 아니다. 실제로 붙는 것은 언제나 하나뿐이다.
    public static class IapReceiptValidatorRegistry
    {
        public static IReceiptValidator Current { get; set; }

        internal static IReceiptValidator ResolveOrDefault() => Current ?? new NoopReceiptValidator();

        // 테스트 전용.
        internal static void Reset() => Current = null;
    }
}
