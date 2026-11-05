namespace DarkNaku.FoundationDI
{
    // 영수증 검증 seam. 실패하면 지급도 확정도 하지 않는다.
    //
    // 동기인 이유: 로컬 검증(CrossPlatformValidator)은 동기다. 서버 검증은 별도의 비동기 seam
    // IAsyncReceiptValidator로 둔다(0.17) — 이 검증을 통과한 구매만 그쪽으로 간다.
    public interface IReceiptValidator
    {
        bool Validate(IAPPurchase purchase, out IAPError error);
    }
}
