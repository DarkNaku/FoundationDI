#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace DarkNaku.FoundationDI
{
    // "지금 ATT를 요청해도 iOS가 받아 주는가"를 답하는 seam.
    //
    // Application.isFocused로는 판정할 수 없다. Unity 6.3 트램펄린은 UIScene 라이프사이클에서
    // UnitySetPlayerFocus(1)을 sceneDidBecomeActive보다 먼저 부르므로, 포커스가 1인데도 앱은
    // 아직 Inactive인 구간이 있다 — 첫 씬의 Awake/Start가 바로 그 구간이다.
    internal interface IAppActivity
    {
        // UIApplication.applicationState == UIApplicationStateActive.
        bool IsActive { get; }

        // UIApplicationWillResignActiveNotification 누적 횟수. 요청 전후로 값이 바뀌었으면
        // 그 사이 시스템 팝업(ATT든 다른 권한이든)이 떴다는 뜻이다.
        int ResignActiveCount { get; }
    }

    internal class NativeAppActivity : IAppActivity
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Att_IsApplicationActive();
        [DllImport("__Internal")] private static extern int FDI_Att_ResignActiveCount();

        public bool IsActive => FDI_Att_IsApplicationActive();

        public int ResignActiveCount => FDI_Att_ResignActiveCount();
#else
        // asmdef가 에디터도 포함한다. 에디터에서는 바인딩이 지원하지 않는다고 답해 이 경로까지
        // 오지 않지만, 오더라도 늘 Active인 앱처럼 동작해 흐름이 막히지 않게 한다.
        public bool IsActive => true;

        public int ResignActiveCount => 0;
#endif
    }
}
