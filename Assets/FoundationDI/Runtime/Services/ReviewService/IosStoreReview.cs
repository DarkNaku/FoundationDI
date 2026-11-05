namespace DarkNaku.FoundationDI
{
    // StoreKit 별점 창(SKStoreReviewController). iOS는 1년에 최대 3번만 띄우고, TestFlight에서는 띄우지 않는다.
    // 준비할 것이 없다.
    //
    // UnityEngine.iOS는 Android 플레이어 빌드에 없다(에디터에서는 컴파일된다). 호출부만 가린다.
    public sealed class IosStoreReview : IStoreReview
    {
        public void Prepare()
        {
        }

#if UNITY_IOS
        public bool Request() => UnityEngine.iOS.Device.RequestStoreReview();
#else
        public bool Request() => false;
#endif
    }
}
