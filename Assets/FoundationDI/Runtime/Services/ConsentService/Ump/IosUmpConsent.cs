#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal sealed class IosUmpConsent : IUmpConsent
    {
        // Inactive 구간을 지난 직후의 장면 전환에 폼이 버려지지 않도록 잠깐 기다린다(ATT와 같은 이유).
        private const float SettleSeconds = 0.5f;

        private delegate void UmpCallback(int requestId, bool success, string message);

        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_IsAvailable();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_IsApplicationActive();
        [DllImport("__Internal")] private static extern int FDI_Ump_GdprApplies();
        [DllImport("__Internal")] private static extern string FDI_Ump_PurposeConsents();
        [DllImport("__Internal")] private static extern int FDI_Ump_ConsentStatus();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_CanRequestAds();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_IsPrivacyOptionsRequired();
        [DllImport("__Internal")] private static extern void FDI_Ump_RequestUpdate(int requestId, int debugGeography, string testDeviceIdsCsv, [MarshalAs(UnmanagedType.I1)] bool tagForUnderAgeOfConsent, UmpCallback callback);
        [DllImport("__Internal")] private static extern void FDI_Ump_LoadAndShowIfRequired(int requestId, UmpCallback callback);
        [DllImport("__Internal")] private static extern void FDI_Ump_ShowPrivacyOptions(int requestId, UmpCallback callback);

        // 네이티브 콜백은 정적 함수만 받을 수 있다(IL2CPP). 요청 ID로 완료 소스를 찾는다.
        private static readonly Dictionary<int, Action<bool, string>> Pending = new();
        private static int _nextRequestId;

        private readonly IMainThreadDispatcher _dispatcher;

        public IosUmpConsent(IMainThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public static bool IsAvailable => FDI_Ump_IsAvailable();

        public bool IsSupported => true;
        public UmpConsentStatus Status => UmpStatusMapper.FromIos(FDI_Ump_ConsentStatus());
        public bool CanRequestAds => FDI_Ump_CanRequestAds();
        public bool IsPrivacyOptionsRequired => FDI_Ump_IsPrivacyOptionsRequired();

        public TcfSignals ReadTcf()
        {
            var gdpr = FDI_Ump_GdprApplies();
            bool? gdprApplies = gdpr < 0 ? (bool?)null : gdpr == 1;

            return new TcfSignals(gdprApplies, FDI_Ump_PurposeConsents());
        }

        public Awaitable<bool> UpdateAsync(UmpRequestOptions options)
        {
            var ids = options.TestDeviceHashedIds == null ? string.Empty : string.Join(",", options.TestDeviceHashedIds);

            return Call(id => FDI_Ump_RequestUpdate(id, UmpStatusMapper.ToIosGeography(options.DebugGeography), ids,
                                                    options.TagForUnderAgeOfConsent, OnNativeCallback));
        }

        public Awaitable<bool> LoadAndShowIfRequiredAsync() =>
            WhenActive(() => Call(id => FDI_Ump_LoadAndShowIfRequired(id, OnNativeCallback)));

        public Awaitable<bool> ShowPrivacyOptionsAsync() =>
            WhenActive(() => Call(id => FDI_Ump_ShowPrivacyOptions(id, OnNativeCallback)));

        private Awaitable<bool> Call(Action<int> invoke)
        {
            var source = new AwaitableCompletionSource<bool>();
            var requestId = ++_nextRequestId;

            Pending[requestId] = (success, message) =>
            {
                if (!success) Debug.LogWarning($"[ConsentService] UMP iOS 호출 실패: {message}");
                source.TrySetResult(success);
            };

            invoke(requestId);
            return source.Awaitable;
        }

        // 앱이 Active가 되고 안정화될 때까지 기다린 뒤 실행한다.
        private Awaitable<bool> WhenActive(Func<Awaitable<bool>> run)
        {
            var source = new AwaitableCompletionSource<bool>();

            async void Forward()
            {
                source.TrySetResult(await run());
            }

            void Check()
            {
                if (!FDI_Ump_IsApplicationActive())
                {
                    _dispatcher.NextFrames(1, Check);
                    return;
                }

                _dispatcher.Delay(SettleSeconds, () =>
                {
                    if (!FDI_Ump_IsApplicationActive())
                    {
                        _dispatcher.NextFrames(1, Check);
                        return;
                    }

                    Forward();
                });
            }

            Check();
            return source.Awaitable;
        }

        [MonoPInvokeCallback(typeof(UmpCallback))]
        private static void OnNativeCallback(int requestId, bool success, string message)
        {
            // 네이티브가 dispatch_get_main_queue로 넘기므로 Unity 메인 스레드다.
            if (!Pending.TryGetValue(requestId, out var complete)) return;

            Pending.Remove(requestId);
            complete(success, message);
        }
    }
}
#endif
