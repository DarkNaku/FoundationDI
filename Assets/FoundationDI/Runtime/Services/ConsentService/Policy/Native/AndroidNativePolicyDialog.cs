#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Plugins/Android/FoundationDIPolicyDialog.androidlib 의 FDIPolicyDialog(AlertDialog)를 부른다. UMP와 달리 Java 소스를 두는 이유:
    // 안드로이드 SDK만 쓰므로 의존성 문제로 Gradle이 깨질 일이 없고, UI 스레드 처리와 링크 스팬을
    // JNI 호출로 조립하는 것보다 훨씬 단순하다.
    internal sealed class AndroidNativePolicyDialog : INativePolicyDialog
    {
        private const string DialogClass = "com.darknaku.foundationdi.consent.FDIPolicyDialog";

        private readonly IMainThreadDispatcher _dispatcher;
        private readonly AndroidJavaObject _activity;
        private readonly AndroidJavaClass _dialog;

        private AndroidNativePolicyDialog(IMainThreadDispatcher dispatcher, AndroidJavaObject activity,
                                          AndroidJavaClass dialog)
        {
            _dispatcher = dispatcher;
            _activity = activity;
            _dialog = dialog;
        }

        // Java 클래스가 빌드에서 빠졌으면 null.
        public static AndroidNativePolicyDialog TryCreate(IMainThreadDispatcher dispatcher)
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                return new AndroidNativePolicyDialog(dispatcher, activity, new AndroidJavaClass(DialogClass));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 네이티브 약관 창(Android)을 찾지 못했다. 프리팹 창을 쓴다: {e.Message}");
                return null;
            }
        }

        public bool IsSupported => true;

        public void Show(PolicyAgreementContent content, PolicyMessage message, Action onAccepted)
        {
            var count = message.Links.Count;
            var starts = new int[count];
            var lengths = new int[count];
            var urls = new string[count];

            for (var i = 0; i < count; i++)
            {
                starts[i] = message.Links[i].Start;
                lengths[i] = message.Links[i].Length;
                urls[i] = message.Links[i].Url;
            }

            _dialog.CallStatic("show", _activity, content.Title ?? string.Empty, message.Text, starts, lengths, urls,
                               content.AcceptLabel ?? "OK", new Listener(() => _dispatcher.Post(onAccepted)));
        }

        public void Close() => _dialog.CallStatic("close", _activity);

        // 콜백은 UI 스레드에서 온다. 디스패처로 메인 스레드에 넘긴다.
        private sealed class Listener : AndroidJavaProxy
        {
            private readonly Action _onAccepted;

            public Listener(Action onAccepted) : base(DialogClass + "$Listener")
            {
                _onAccepted = onAccepted;
            }

            // Java 인터페이스 메서드 이름 그대로다.
            public void onAccepted() => _onAccepted();
        }
    }
}
#endif
