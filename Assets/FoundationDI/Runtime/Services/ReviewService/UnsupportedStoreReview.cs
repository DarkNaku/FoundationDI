using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 에디터·미지원 플랫폼. 요청을 로그로만 남긴다 — 게임의 "리뷰 요청 시점" 흐름을 에디터에서 확인할 수 있다.
    public sealed class UnsupportedStoreReview : IStoreReview
    {
        public void Prepare()
        {
        }

        public bool Request()
        {
            Debug.Log("[ReviewService] 리뷰 창 요청 — 이 환경에서는 띄우지 않는다");
            return true;
        }
    }
}
