#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Plugins/iOS/FDI_PolicyDialog.mm 의 커스텀 알림창을 부른다. UIAlertController는 본문에 링크를
    // 거는 공개 API가 없어 같은 모양의 뷰 컨트롤러를 직접 띄운다.
    internal sealed class IosNativePolicyDialog : INativePolicyDialog
    {
        // Inactive 구간을 지난 직후의 장면 전환에 모달이 버려지지 않도록 잠깐 기다린다(UMP·ATT와 같은 이유).
        private const float SettleSeconds = 0.5f;

        private delegate void AcceptedCallback();

        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_PolicyDialog_IsApplicationActive();
        [DllImport("__Internal")] private static extern void FDI_PolicyDialog_Show(string title, string message, int[] starts, int[] lengths, string[] urls, int linkCount, string acceptLabel, AcceptedCallback callback);
        [DllImport("__Internal")] private static extern void FDI_PolicyDialog_Close();

        // 네이티브 콜백은 정적 함수만 받을 수 있다(IL2CPP).
        private static Action _onAccepted;

        private readonly IMainThreadDispatcher _dispatcher;
        private IDisposable _waiting;

        public IosNativePolicyDialog(IMainThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public bool IsSupported => true;

        public void Show(PolicyAgreementContent content, PolicyMessage message, Action onAccepted)
        {
            _onAccepted = onAccepted;
            WhenActive(() => Present(content, message));
        }

        public void Close()
        {
            _waiting?.Dispose();
            _waiting = null;
            _onAccepted = null;
            FDI_PolicyDialog_Close();
        }

        private static void Present(PolicyAgreementContent content, PolicyMessage message)
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

            FDI_PolicyDialog_Show(content.Title ?? string.Empty, message.Text, starts, lengths, urls, count,
                                  content.AcceptLabel ?? "OK", OnNativeAccepted);
        }

        // 첫 씬은 Inactive 구간에서 로드되고, 그때 띄운 모달은 화면에 나오지 않는다.
        private void WhenActive(Action run)
        {
            void Check()
            {
                if (!FDI_PolicyDialog_IsApplicationActive())
                {
                    _waiting = _dispatcher.NextFrames(1, Check);
                    return;
                }

                _waiting = _dispatcher.Delay(SettleSeconds, () =>
                {
                    if (!FDI_PolicyDialog_IsApplicationActive())
                    {
                        _waiting = _dispatcher.NextFrames(1, Check);
                        return;
                    }

                    _waiting = null;
                    run();
                });
            }

            Check();
        }

        // 버튼 탭은 UIKit 메인 스레드 = Unity 메인 스레드다.
        [MonoPInvokeCallback(typeof(AcceptedCallback))]
        private static void OnNativeAccepted()
        {
            var callback = _onAccepted;
            _onAccepted = null;
            callback?.Invoke();
        }
    }
}
#endif
