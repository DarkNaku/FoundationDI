using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 스토어(Google Play·App Store)에 새 버전이 올라왔는지 서버 없이 확인하고 스토어로 보낸다.
    //
    // 언제 확인할지, 팝업을 띄울지, 업데이트를 강제할지는 게임이 정한다. 강제할 때는 Available일 때만
    // 막아야 한다 — 단계적 출시에서 빠진 유저나 App Store 조회 캐시가 늦은 유저는 스토어에도 새 버전이
    // 보이지 않아 갇힌다.
    public interface IAppUpdateService : IDisposable
    {
        // 실패해도 던지지 않고 Unknown을 돌려준다.
        Awaitable<AppUpdateCheckResult> CheckAsync();

        // 스토어의 앱 페이지를 연다. 열었으면 true.
        bool OpenStore();
    }
}
