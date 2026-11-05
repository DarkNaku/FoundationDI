using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 창을 OS 네이티브 창으로 띄운다. 본문 속 "이용약관"·"개인정보 처리방침"이 링크다.
    // 네이티브 창을 띄울 수 없는 곳(에디터, 플러그인 누락)에서는 대체 창(기본은 프리팹 창)을 쓴다.
    public sealed class NativePolicyAgreementPrompt : IPolicyAgreementPrompt
    {
        private readonly INativePolicyDialog _dialog;
        private readonly IPolicyAgreementPrompt _fallback;

        private AwaitableCompletionSource _pending;

        public NativePolicyAgreementPrompt(INativePolicyDialog dialog, IPolicyAgreementPrompt fallback)
        {
            _dialog = dialog;
            _fallback = fallback;
        }

        public Awaitable ShowAsync(PolicyAgreementContent content)
        {
            Close();

            if (_dialog == null || !_dialog.IsSupported) return _fallback.ShowAsync(content);

            var pending = new AwaitableCompletionSource();
            _pending = pending;

            // 닫힌 뒤 늦게 도착한 OK가 다음 표시를 완료시키지 않도록 표시마다 자기 완료 소스만 본다.
            _dialog.Show(content, PolicyMessageFormatter.Format(content), () =>
            {
                if (_pending != pending) return;

                _pending = null;
                pending.SetResult();
            });

            return pending.Awaitable;
        }

        public void Close()
        {
            if (_pending != null)
            {
                _pending = null;
                _dialog.Close();
            }

            _fallback.Close();
        }
    }
}
