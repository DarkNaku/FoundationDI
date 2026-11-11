namespace DarkNaku.FoundationDI
{
    // 원스토어의 기본 로컬 검증. 통과시킨다 — SDK가 라이선스 키(공개 키)로 구매 서명을 이미 검증하고,
    // 서명이 맞지 않는 구매는 ERROR_SIGNATURE_VERIFICATION으로 실패시켜 여기까지 오지 않는다.
    // CrossPlatformReceiptValidator(구글 Tangle)는 원스토어 영수증을 검증할 수 없어 쓰지 않는다.
    // 더 강한 보증이 필요하면 IAsyncReceiptValidator로 원스토어 서버 API에 묻는다(영수증에 구매 토큰이 있다).
    public sealed class OneStoreReceiptValidator : IReceiptValidator
    {
        public bool Validate(IAPPurchase purchase, out IAPError error)
        {
            error = default;
            return true;
        }
    }
}
