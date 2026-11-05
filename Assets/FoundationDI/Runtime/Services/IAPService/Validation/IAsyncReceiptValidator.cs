using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum IAPValidationStatus
    {
        Valid,       // 지급한다
        Invalid,     // 위조·재사용 영수증 — 지급하지 않는다(처리 방식은 IAPServiceOptions.InvalidReceiptPolicy)
        RetryLater,  // 서버 장애 등 지금은 판단할 수 없다 — 확정하지 않고 ProcessUnconfirmedAsync·다음 실행에 다시 묻는다
    }

    public readonly struct IAPValidationResult
    {
        public IAPValidationStatus Status { get; }
        public string Message { get; }

        private IAPValidationResult(IAPValidationStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public static IAPValidationResult Valid() => new(IAPValidationStatus.Valid, null);
        public static IAPValidationResult Invalid(string message) => new(IAPValidationStatus.Invalid, message);
        public static IAPValidationResult RetryLater(string message) => new(IAPValidationStatus.RetryLater, message);
    }

    // 서버 영수증 검증 seam. 등록하면 로컬 검증(IReceiptValidator)을 통과한 구매만 여기로 오고, Valid일 때만 지급한다.
    //
    // 지급 처리기 안에서 서버를 부르면 "검증 실패"와 "지급 실패"가 섞이고 "지금은 모르겠다"를 표현할 수 없다.
    // 그래서 검증을 지급 앞의 별도 단계로 둔다. 예외는 RetryLater로 본다.
    public interface IAsyncReceiptValidator
    {
        Awaitable<IAPValidationResult> ValidateAsync(IAPPurchase purchase);
    }
}
