using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 코어는 이 어셈블리를 참조할 수 없으므로 이쪽이 먼저 자신을 알린다.
    //
    // BeforeSceneLoad여야 한다. 씬 IInstaller가 RegisterAdService를 부르는 시점보다
    // 확실히 앞서야 AdService가 이 구현을 집어 갈 수 있다.
    public static class IosSupportInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            TrackingAuthorizationRegistry.Register(dispatcher => new AttTrackingAuthorization(dispatcher));
        }
    }
}
