using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Google Play In-App Review(com.google.android.play:review)를 JNI로 부른다.
    //
    // Google의 Unity 플러그인은 쓰지 않는다. EDM4U를 UPM으로 함께 들이는데, LevelPlay 등이 Assets에
    // 넣은 EDM4U와 겹친다. 라이브러리는 ReviewServiceSettings의 Use Play In-App Review가 만드는 의존성
    // XML로 EDM4U가 받는다.
    //
    // ReviewInfo는 잠시만 유효해 띄우기 직전(Prepare)에 받아 둔다. 받아 둔 것이 없으면 받은 뒤 바로 띄운다.
    // Task 결과는 안드로이드 UI 스레드로 오므로 디스패처로 유니티 메인 스레드에 넘겨 상태를 한 스레드에서만 만진다.
    public sealed class AndroidStoreReview : IStoreReview, IDisposable
    {
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly AndroidJavaObject _activity;
        private readonly AndroidJavaObject _manager;

        private AndroidJavaObject _reviewInfo;
        private bool _isPreparing;
        private bool _isLaunchPending;

        public AndroidStoreReview(IMainThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher;

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            using var factory = new AndroidJavaClass("com.google.android.play.core.review.ReviewManagerFactory");
            _manager = factory.CallStatic<AndroidJavaObject>("create", _activity);
        }

        public void Prepare()
        {
            if (_reviewInfo != null || _isPreparing) return;

            _isPreparing = true;

            using var task = _manager.Call<AndroidJavaObject>("requestReviewFlow");
            task.Call<AndroidJavaObject>("addOnCompleteListener", new CompleteListener(OnReviewFlowRequested)).Dispose();
        }

        public bool Request()
        {
            if (_reviewInfo == null)
            {
                _isLaunchPending = true;
                Prepare();
                return true;
            }

            Launch();
            return true;
        }

        public void Dispose()
        {
            _reviewInfo?.Dispose();
            _reviewInfo = null;
            _manager?.Dispose();
            _activity?.Dispose();
        }

        private void Launch()
        {
            var reviewInfo = _reviewInfo;
            _reviewInfo = null;
            _isLaunchPending = false;

            using var task = _manager.Call<AndroidJavaObject>("launchReviewFlow", _activity, reviewInfo);
            reviewInfo.Dispose();
        }

        // UI 스레드에서 온다. 결과만 읽어 메인 스레드로 넘긴다.
        private void OnReviewFlowRequested(AndroidJavaObject task)
        {
            AndroidJavaObject reviewInfo = null;
            string error = null;

            try
            {
                if (task.Call<bool>("isSuccessful")) reviewInfo = task.Call<AndroidJavaObject>("getResult");
                else error = "리뷰 정보를 받지 못했다";
            }
            catch (Exception e)
            {
                error = e.Message;
            }

            if (_dispatcher != null) _dispatcher.Post(() => ApplyReviewInfo(reviewInfo, error));
            else ApplyReviewInfo(reviewInfo, error);
        }

        private void ApplyReviewInfo(AndroidJavaObject reviewInfo, string error)
        {
            _isPreparing = false;

            if (reviewInfo == null)
            {
                Debug.LogWarning($"[ReviewService] {error}");
                _isLaunchPending = false;
                return;
            }

            _reviewInfo = reviewInfo;

            if (!_isLaunchPending) return;

            try
            {
                Launch();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReviewService] 리뷰 창을 띄우지 못했다: {e.Message}");
            }
        }

        // com.google.android.gms.tasks.OnCompleteListener — 메서드 이름은 자바 인터페이스를 따른다.
        private sealed class CompleteListener : AndroidJavaProxy
        {
            private readonly Action<AndroidJavaObject> _completed;

            public CompleteListener(Action<AndroidJavaObject> completed)
                : base("com.google.android.gms.tasks.OnCompleteListener")
            {
                _completed = completed;
            }

            // ReSharper disable once InconsistentNaming
            public void onComplete(AndroidJavaObject task) => _completed(task);
        }
    }
}
