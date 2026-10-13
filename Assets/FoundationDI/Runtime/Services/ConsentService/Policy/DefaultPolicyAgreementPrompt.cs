using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    // 앱 수명 UI라 UINavigator(씬 수명)를 쓰지 않고 자체 캔버스를 연다(DummyAdCanvas와 같은 방식).
    public sealed class DefaultPolicyAgreementPrompt : IPolicyAgreementPrompt
    {
        // 게임 UI와 광고 더미 캔버스보다 위.
        private const int SortingOrder = 32000;

        private readonly PolicyAgreementView _prefab;
        private readonly IUrlOpener _opener;
        private readonly TemporaryEventSystem _eventSystem = new();

        private AwaitableCompletionSource _pending;

        public DefaultPolicyAgreementPrompt(PolicyAgreementView prefab, IUrlOpener opener)
        {
            _prefab = prefab;
            _opener = opener ?? new UnityUrlOpener();
        }

        internal PolicyAgreementView CurrentView { get; private set; }

        public Awaitable ShowAsync(PolicyAgreementContent content)
        {
            // 조용히 완료하면 사용자가 보지도 않은 약관을 수락한 것으로 저장된다.
            if (_prefab == null)
            {
                throw new InvalidOperationException(
                    "[ConsentService] 약관 창 프리팹이 없다. ConsentServiceSettings의 Prompt Prefab을 지정한다.");
            }

            Close();

            CurrentView = Object.Instantiate(_prefab);
            CurrentView.name = "[ConsentService] PolicyAgreement";

            var canvas = CurrentView.GetComponentInChildren<Canvas>(true);

            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
            }

            if (Application.isPlaying) Object.DontDestroyOnLoad(CurrentView.gameObject);

            _eventSystem.Acquire();

            CurrentView.Accepted += OnAccepted;
            CurrentView.LinkRequested += _opener.Open;
            CurrentView.Bind(content);

            _pending = new AwaitableCompletionSource();
            return _pending.Awaitable;
        }

        public void Close()
        {
            if (CurrentView != null)
            {
                CurrentView.Accepted -= OnAccepted;
                CurrentView.LinkRequested -= _opener.Open;

                if (Application.isPlaying) Object.Destroy(CurrentView.gameObject);
                else Object.DestroyImmediate(CurrentView.gameObject);
            }

            CurrentView = null;
            _eventSystem.Release();
        }

        private void OnAccepted()
        {
            var pending = _pending;
            _pending = null;

            Close();
            pending?.SetResult();
        }
    }
}
