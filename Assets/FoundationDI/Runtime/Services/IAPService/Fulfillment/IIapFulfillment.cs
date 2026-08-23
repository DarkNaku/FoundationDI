using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 구매를 실제 재화/권한으로 바꾸는 지점. 게임이 구현해 DI에 등록한다.
    //
    // 신규 구매, 앱 재시작 때 발견된 미확정 구매, 복원 — 셋 다 이 한 메서드로 들어온다.
    // 그래서 게임은 "지급" 로직을 한 곳에만 쓰면 된다.
    //
    // true를 반환해야 스토어에 확정(Confirm)이 전달된다. 저장에 실패했다면 false를 반환할 것.
    // 확정하지 않으면 스토어가 다음 실행에 같은 구매를 다시 내려주므로 재화가 유실되지 않는다.
    public interface IIapFulfillment
    {
        Awaitable<bool> FulfillAsync(IapPurchase purchase);
    }
}
