using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 리뷰 창 때문에 게임 흐름이 끊기면 안 되므로 예외는 삼키고 경고만 남긴다.
    public sealed class ReviewService : IReviewService
    {
        private readonly Func<IStoreReview> _storeFactory;

        private IStoreReview _store;
        private bool _isBroken;

        // 창구는 처음 쓸 때 만든다. Android 창구는 생성에서 JNI로 ReviewManager를 만드는데, Play Review
        // 라이브러리가 빌드에 없으면 여기서 던진다 — 그 실패가 DI 해석(앱 시작)으로 번지지 않게 한다.
        public ReviewService(Func<IStoreReview> storeFactory)
        {
            _storeFactory = storeFactory;
        }

        public void Prepare()
        {
            try
            {
                Store?.Prepare();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReviewService] 리뷰 창을 준비하지 못했다: {e.Message}");
            }
        }

        public bool Request()
        {
            try
            {
                return Store?.Request() ?? false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReviewService] 리뷰 창을 요청하지 못했다: {e.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            (_store as IDisposable)?.Dispose();
            _store = null;
        }

        // 만들다 실패하면 다시 시도하지 않는다. 같은 예외 로그가 요청마다 쌓일 뿐이다.
        private IStoreReview Store
        {
            get
            {
                if (_store != null || _isBroken) return _store;

                try
                {
                    _store = _storeFactory();
                }
                catch (Exception e)
                {
                    _isBroken = true;
                    Debug.LogWarning($"[ReviewService] 리뷰 창구를 만들지 못했다: {e.Message}");
                }

                return _store;
            }
        }
    }
}
