using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 게임이 IIapFulfillment를 등록하지 않았을 때의 폴백. 지급을 하지 않고 곧바로 확정한다.
    //
    // 재화 지급이 없는 상품(광고 제거처럼 소유 여부만 보면 되는 것)이나 프로토타입에는 이걸로 충분하다.
    // 소모성 재화를 다루기 시작하면 반드시 자기 구현으로 교체해야 한다 — 지급을 저장하기 전에
    // 확정해 버리면 저장 실패 시 구매가 증발한다.
    public sealed class AutoConfirmFulfillment : IIapFulfillment
    {
        public Awaitable<bool> FulfillAsync(IapPurchase purchase)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }
    }
}
