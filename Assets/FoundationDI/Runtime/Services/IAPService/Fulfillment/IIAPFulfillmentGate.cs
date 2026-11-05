namespace DarkNaku.FoundationDI
{
    // 지급 처리기가 스스로 준비 여부를 알리는 선택 인터페이스. IIAPFulfillment와 같은 객체에 구현한다.
    //
    // 진행 데이터를 씬에서 늦게 불러오는 게임은 앱 시작 직후 재전달된 미확정 구매를 지급할 수 없다.
    // false를 돌려주면 그 구매는 다음 실행까지 다시 오지 않으므로, 준비 전에 온 주문은 서비스가 붙잡아 두고
    // 게임이 준비를 마친 뒤 IIAPService.ProcessUnconfirmedAsync()를 부르면 그때 지급한다.
    // 구현하지 않은 지급 처리기는 늘 준비된 것으로 본다.
    public interface IIAPFulfillmentGate
    {
        bool IsReady { get; }
    }
}
