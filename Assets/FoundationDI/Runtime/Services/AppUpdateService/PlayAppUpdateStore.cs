using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Google Play In-App Updates(com.google.android.play:app-update)를 JNI로 부른다. 확인만 하고 인앱 업데이트
    // 흐름은 쓰지 않는다 — 스토어 이동은 iOS와 같이 스토어 페이지를 연다.
    //
    // Google의 Unity 플러그인은 쓰지 않는다(AndroidStoreReview와 같은 이유). 라이브러리는
    // AppUpdateServiceSettings의 Use Play App Update가 만드는 의존성 XML로 EDM4U가 받는다.
    //
    // Play 스토어에서 설치하지 않은 빌드(사이드로드·에디터 빌드)는 Task가 실패한다(ERROR_APP_NOT_OWNED) —
    // 서비스가 경고 후 Unknown으로 바꾼다. 테스트는 내부 앱 공유로 설치해야 한다.
    // Task 결과는 안드로이드 UI 스레드로 오므로 디스패처로 유니티 메인 스레드에 넘겨 완료한다.
    public sealed class PlayAppUpdateStore : IAppUpdateStore, IDisposable
    {
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly AndroidJavaObject _activity;
        private readonly AndroidJavaObject _manager;

        public PlayAppUpdateStore(IMainThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher;

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            using var factory = new AndroidJavaClass("com.google.android.play.core.appupdate.AppUpdateManagerFactory");
            _manager = factory.CallStatic<AndroidJavaObject>("create", _activity);
        }

        public Awaitable<AppUpdateCheckResult> CheckAsync()
        {
            var source = new AwaitableCompletionSource<AppUpdateCheckResult>();

            using var task = _manager.Call<AndroidJavaObject>("getAppUpdateInfo");
            task.Call<AndroidJavaObject>("addOnCompleteListener",
                new CompleteListener(completed => OnAppUpdateInfo(completed, source))).Dispose();

            return source.Awaitable;
        }

        public bool OpenStore()
        {
            Application.OpenURL(PlayAppUpdateMapper.StoreUrl(Application.identifier));
            return true;
        }

        public void Dispose()
        {
            _manager?.Dispose();
            _activity?.Dispose();
        }

        // UI 스레드에서 온다. 결과만 읽어 메인 스레드로 넘긴다.
        private void OnAppUpdateInfo(AndroidJavaObject task, AwaitableCompletionSource<AppUpdateCheckResult> source)
        {
            AppUpdateCheckResult result = default;
            Exception error = null;

            try
            {
                if (task.Call<bool>("isSuccessful"))
                {
                    using var info = task.Call<AndroidJavaObject>("getResult");
                    result = PlayAppUpdateMapper.ToResult(
                        info.Call<int>("updateAvailability"),
                        ReadNullableInt(info, "clientVersionStalenessDays"),
                        info.Call<int>("updatePriority"));
                }
                else
                {
                    using var exception = task.Call<AndroidJavaObject>("getException");
                    error = new InvalidOperationException(exception?.Call<string>("toString") ?? "업데이트 정보를 받지 못했다");
                }
            }
            catch (Exception e)
            {
                error = e;
            }

            void Complete()
            {
                if (error != null) source.SetException(error);
                else source.SetResult(result);
            }

            if (_dispatcher != null) _dispatcher.Post(Complete);
            else Complete();
        }

        // java.lang.Integer — 업데이트가 없거나 정보가 없으면 null이다.
        private static int? ReadNullableInt(AndroidJavaObject target, string method)
        {
            using var boxed = target.Call<AndroidJavaObject>(method);
            return boxed?.Call<int>("intValue");
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
