using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 플랫폼 스토어 창구 seam. Android는 Play In-App Updates, iOS는 iTunes Lookup,
    // 에디터·미지원 플랫폼은 UnsupportedAppUpdateStore다(AppUpdateStoreFactory가 고른다).
    public interface IAppUpdateStore
    {
        Awaitable<AppUpdateCheckResult> CheckAsync();

        bool OpenStore();
    }
}
