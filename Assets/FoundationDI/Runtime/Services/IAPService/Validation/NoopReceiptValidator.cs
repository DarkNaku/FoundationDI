namespace DarkNaku.FoundationDI
{
    // 코어 asmdef는 UnityEngine.Purchasing.Security를 참조하지 않으므로 실제 검증을 할 수 없다.
    // 실제 검증기는 FoundationDI.UnityIAP 어셈블리가 IapReceiptValidatorRegistry에 등록한다.
    public sealed class NoopReceiptValidator : IReceiptValidator
    {
        public bool Validate(IapPurchase purchase, out IapError error)
        {
            error = default;
            return true;
        }
    }
}
