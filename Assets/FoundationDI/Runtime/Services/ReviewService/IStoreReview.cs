namespace DarkNaku.FoundationDI
{
    // 플랫폼 리뷰 창구 seam. Android는 AndroidStoreReview, iOS는 IosStoreReview,
    // 에디터·미지원 플랫폼은 UnsupportedStoreReview다(StoreReviewFactory가 고른다).
    public interface IStoreReview
    {
        void Prepare();

        bool Request();
    }
}
