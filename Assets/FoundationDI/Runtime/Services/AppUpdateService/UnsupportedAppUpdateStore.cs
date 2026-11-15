using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 에디터·미지원 플랫폼. 스토어가 없으니 확인하지 못했다(Unknown)로 답한다 — UpToDate로 답하면
    // 게임이 "최신이다"로 오해해 에디터에서 강제 업데이트 분기를 시험할 수 없다.
    public sealed class UnsupportedAppUpdateStore : IAppUpdateStore
    {
        public Awaitable<AppUpdateCheckResult> CheckAsync()
        {
            var source = new AwaitableCompletionSource<AppUpdateCheckResult>();
            source.SetResult(AppUpdateCheckResult.Unknown());
            return source.Awaitable;
        }

        public bool OpenStore()
        {
            Debug.Log("[AppUpdateService] 스토어 열기 요청 — 이 환경에서는 열지 않는다");
            return false;
        }
    }
}
