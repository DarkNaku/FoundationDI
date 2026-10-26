using UnityEngine.iOS;

namespace DarkNaku.FoundationDI
{
    // StoreKit 별점 창(SKStoreReviewController). iOS는 1년에 최대 3번만 띄우고, TestFlight에서는 띄우지 않는다.
    // 준비할 것이 없다.
    public sealed class IosStoreReview : IStoreReview
    {
        public void Prepare()
        {
        }

        public bool Request() => Device.RequestStoreReview();
    }
}
