#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // UMP Android SDK(com.google.android.ump)를 JNI로 직접 부른다.
    //
    // Java 소스를 두지 않는 이유: UMP 클래스를 참조하는 .java는 UMP를 끈 프로젝트에서 Gradle
    // 컴파일을 깨뜨린다. JNI 호출은 실행 시점에만 클래스를 찾으므로 의존성이 없어도 빌드된다.
    //
    // UMP 콜백은 메인 스레드 보장이 없다(UI 스레드에서 온다). 전부 디스패처로 넘긴다.
    internal sealed class AndroidUmpConsent : IUmpConsent
    {
        private const string UmpClass = "com.google.android.ump.UserMessagingPlatform";
        private const string Package = "com.google.android.ump.";

        private readonly IMainThreadDispatcher _dispatcher;
        private readonly AndroidJavaObject _activity;
        private readonly AndroidJavaObject _info;

        private AndroidUmpConsent(IMainThreadDispatcher dispatcher, AndroidJavaObject activity, AndroidJavaObject info)
        {
            _dispatcher = dispatcher;
            _activity = activity;
            _info = info;
        }

        // UMP aar가 빌드에 없으면(의존성 XML 미생성, EDM4U 미해석) null.
        public static AndroidUmpConsent TryCreate(IMainThreadDispatcher dispatcher)
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                using var ump = new AndroidJavaClass(UmpClass);
                var info = ump.CallStatic<AndroidJavaObject>("getConsentInformation", activity);

                return new AndroidUmpConsent(dispatcher, activity, info);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ConsentService] UMP Android SDK를 찾지 못했다. UMP 없이 진행한다. " +
                                 $"Use UMP를 켰다면 EDM4U로 Android 의존성을 해석했는지 확인한다: {e.Message}");
                return null;
            }
        }

        public bool IsSupported => true;

        public UmpConsentStatus Status => UmpStatusMapper.FromAndroid(_info.Call<int>("getConsentStatus"));

        public bool CanRequestAds => _info.Call<bool>("canRequestAds");

        public bool IsPrivacyOptionsRequired
        {
            get
            {
                using var status = _info.Call<AndroidJavaObject>("getPrivacyOptionsRequirementStatus");
                return status.Call<string>("name") == "REQUIRED";
            }
        }

        // UMP는 IAB 키를 앱 기본 SharedPreferences(<패키지명>_preferences)에 쓴다.
        public TcfSignals ReadTcf()
        {
            var name = _activity.Call<string>("getPackageName") + "_preferences";

            using var prefs = _activity.Call<AndroidJavaObject>("getSharedPreferences", name, 0);

            bool? gdprApplies = null;

            if (prefs.Call<bool>("contains", "IABTCF_gdprApplies"))
            {
                gdprApplies = prefs.Call<int>("getInt", "IABTCF_gdprApplies", 0) == 1;
            }

            // 기본값으로 null을 넘기지 않는다. AndroidJNIHelper가 null 인자의 시그니처를 Object로 추론해
            // getString(String, String) 조회가 실패할 수 있고, 그 예외는 SafeRead가 삼켜 TCF가 통째로
            // 비게 된다 — 그러면 목적 1을 거부한 EEA 사용자가 Granted로 판정된다.
            var purposes = prefs.Call<string>("getString", "IABTCF_PurposeConsents", string.Empty);

            return new TcfSignals(gdprApplies, string.IsNullOrEmpty(purposes) ? null : purposes);
        }

        public Awaitable<bool> UpdateAsync(UmpRequestOptions options)
        {
            var source = new AwaitableCompletionSource<bool>();

            RunOnUiThread(() =>
            {
                try
                {
                    using var parameters = BuildParameters(options);

                    _info.Call("requestConsentInfoUpdate", _activity, parameters,
                               new UpdateSuccessListener(() => _dispatcher.Post(() => source.TrySetResult(true))),
                               new UpdateFailureListener(message => _dispatcher.Post(() =>
                               {
                                   Debug.LogWarning($"[ConsentService] UMP 갱신 실패: {message}");
                                   source.TrySetResult(false);
                               })));
                }
                catch (Exception e)
                {
                    _dispatcher.Post(() =>
                    {
                        Debug.LogWarning($"[ConsentService] UMP 갱신 호출 실패: {e.Message}");
                        source.TrySetResult(false);
                    });
                }
            });

            return source.Awaitable;
        }

        public Awaitable<bool> LoadAndShowIfRequiredAsync() => ShowForm("loadAndShowConsentFormIfRequired");

        public Awaitable<bool> ShowPrivacyOptionsAsync() => ShowForm("showPrivacyOptionsForm");

        private Awaitable<bool> ShowForm(string method)
        {
            var source = new AwaitableCompletionSource<bool>();

            RunOnUiThread(() =>
            {
                try
                {
                    using var ump = new AndroidJavaClass(UmpClass);

                    ump.CallStatic(method, _activity, new FormDismissedListener(message => _dispatcher.Post(() =>
                    {
                        if (message != null) Debug.LogWarning($"[ConsentService] UMP 폼 에러({method}): {message}");
                        source.TrySetResult(message == null);
                    })));
                }
                catch (Exception e)
                {
                    _dispatcher.Post(() =>
                    {
                        Debug.LogWarning($"[ConsentService] UMP 폼 호출 실패({method}): {e.Message}");
                        source.TrySetResult(false);
                    });
                }
            });

            return source.Awaitable;
        }

        private AndroidJavaObject BuildParameters(UmpRequestOptions options)
        {
            using var builder = new AndroidJavaObject(Package + "ConsentRequestParameters$Builder");

            builder.Call<AndroidJavaObject>("setTagForUnderAgeOfConsent", options.TagForUnderAgeOfConsent).Dispose();

            var hasIds = options.TestDeviceHashedIds != null && options.TestDeviceHashedIds.Count > 0;

            if (options.DebugGeography != UmpDebugGeography.Disabled || hasIds)
            {
                using var debug = new AndroidJavaObject(Package + "ConsentDebugSettings$Builder", _activity);

                debug.Call<AndroidJavaObject>("setDebugGeography",
                                              UmpStatusMapper.ToAndroidGeography(options.DebugGeography)).Dispose();

                if (hasIds)
                {
                    foreach (var id in options.TestDeviceHashedIds)
                    {
                        debug.Call<AndroidJavaObject>("addTestDeviceHashedId", id).Dispose();
                    }
                }

                using var debugSettings = debug.Call<AndroidJavaObject>("build");
                builder.Call<AndroidJavaObject>("setConsentDebugSettings", debugSettings).Dispose();
            }

            return builder.Call<AndroidJavaObject>("build");
        }

        private void RunOnUiThread(Action action) =>
            _activity.Call("runOnUiThread", new AndroidJavaRunnable(action));

        private sealed class UpdateSuccessListener : AndroidJavaProxy
        {
            private readonly Action _onSuccess;

            public UpdateSuccessListener(Action onSuccess)
                : base(Package + "ConsentInformation$OnConsentInfoUpdateSuccessListener")
            {
                _onSuccess = onSuccess;
            }

            // JNI가 이름으로 부른다. 이름을 바꾸지 않는다.
            public void onConsentInfoUpdateSuccess() => _onSuccess();
        }

        private sealed class UpdateFailureListener : AndroidJavaProxy
        {
            private readonly Action<string> _onFailure;

            public UpdateFailureListener(Action<string> onFailure)
                : base(Package + "ConsentInformation$OnConsentInfoUpdateFailureListener")
            {
                _onFailure = onFailure;
            }

            public void onConsentInfoUpdateFailure(AndroidJavaObject formError) =>
                _onFailure(formError?.Call<string>("getMessage") ?? "unknown");
        }

        private sealed class FormDismissedListener : AndroidJavaProxy
        {
            private readonly Action<string> _onDismissed;

            public FormDismissedListener(Action<string> onDismissed)
                : base(Package + "ConsentForm$OnConsentFormDismissedListener")
            {
                _onDismissed = onDismissed;
            }

            // 에러가 없으면 formError가 null이다.
            public void onConsentFormDismissed(AndroidJavaObject formError) =>
                _onDismissed(formError?.Call<string>("getMessage"));
        }
    }
}
#endif
