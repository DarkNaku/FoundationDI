using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 영수증 검증(로컬·서버)에 실패한 구매를 어떻게 할지.
    public enum InvalidReceiptPolicy
    {
        // 확정하지 않는다. 스토어가 매 실행 다시 주고, 검증 규칙을 고친 버전에서 지급될 수 있다.
        KeepPending,

        // 소모성은 지급 없이 확정해 버린다(위조 영수증이 영원히 재전달되지 않게). 비소모성은 확정하면 스토어의
        // 확정 목록에 올라 소유로 동기화되므로 이 정책에서도 확정하지 않는다.
        ConfirmWithoutGrant,
    }

    // RegisterIAPService에 넘기는 설정. 기본값만으로 0.16 이전과 같이 동작한다.
    //   builder.RegisterIAPService(new IAPServiceOptions { VerboseLogging = true });
    public sealed class IAPServiceOptions
    {
        public bool VerboseLogging { get; set; }

        // 스토어 연결을 기다리는 최대 시간(초). 넘으면 초기화가 false로 끝나고, 다시 부르면 처음부터 시도한다.
        // 0 이하면 기다린다. 스토어 앱 문제·네트워크 끊김에서 이벤트가 오지 않아 로딩이 멈추는 것을 막는다.
        public float ConnectTimeoutSeconds { get; set; } = 30f;

        // 상품 조회·구매 조회를 각각 기다리는 최대 시간(초). 상품 조회가 넘으면 초기화 실패, 구매 조회가 넘으면
        // 미확정 구매를 이번에 못 찾은 것으로 보고 초기화는 성공한다. 0 이하면 기다린다.
        public float FetchTimeoutSeconds { get; set; } = 30f;

        // 구매 결과를 기다리는 최대 시간(초). 넘으면 PurchaseAsync가 Failed(IAPErrorCodes.Timeout)로 끝난다.
        // 결제는 스토어에서 끝났을 수 있어 늦게 온 주문도 지급·확정하고 Purchased를 낸다.
        // 스토어 시트에서 결제 수단을 고르는 시간까지 들어가므로 기본은 끈다(0).
        public float PurchaseTimeoutSeconds { get; set; }

        public InvalidReceiptPolicy InvalidReceiptPolicy { get; set; } = InvalidReceiptPolicy.KeepPending;

        // 지정하면 Unity IAP Catalog 대신 이 상품 목록을 쓴다. 상품을 게임 스펙 표로 관리할 때 표와 Catalog가
        // 따로 노는 것을 막는다. 서비스를 만들 때 한 번 부른다.
        //   CatalogSource = () => shopTable.Paid.Select(row => new IAPProductDefinition(row.Pid, IAPProductType.Consumable))
        public Func<IEnumerable<IAPProductDefinition>> CatalogSource { get; set; }
    }
}
