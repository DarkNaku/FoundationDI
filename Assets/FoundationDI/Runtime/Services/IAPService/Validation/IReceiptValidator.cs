namespace DarkNaku.FoundationDI
{
    // 영수증 검증 seam. 실패하면 지급도 확정도 하지 않는다.
    //
    // 동기인 이유: 로컬 검증(CrossPlatformValidator)은 동기이고, 서버 검증이 필요해지면
    // 이 인터페이스가 아니라 별도의 비동기 seam을 추가하는 편이 정직하다 — 지금 없는
    // 요구를 위해 모든 구현을 async로 만들 이유가 없다.
    public interface IReceiptValidator
    {
        bool Validate(IapPurchase purchase, out IapError error);
    }
}
