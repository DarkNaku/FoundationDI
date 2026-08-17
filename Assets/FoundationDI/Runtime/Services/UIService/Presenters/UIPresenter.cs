using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPresenter
    {
        public enum LifecycleEvent { BeforeShow, AfterShow, BeforeHide, AfterHide }

        internal UIView ViewBase { get; private set; }
        internal IUIElementHost Host { get; private set; }
        internal IUITransition TransitionOverride { get; private set; }

        private Dictionary<LifecycleEvent, List<Action<UIPresenter>>> _subscribers;

        // WithOverlay로 등록된 오버레이 요청(빌더 시점). Page/Popup 표시 시 함께 노출된다.
        private List<(Type type, Action<UIPresenter> configure)> _overlayRequests;
        internal IReadOnlyList<(Type type, Action<UIPresenter> configure)> OverlayRequests => _overlayRequests;
        internal void AddOverlayRequest(Type type, Action<UIPresenter> configure)
            => (_overlayRequests ??= new()).Add((type, configure));

        // 표시 시점에 실제로 스폰되어 이 호스트에 종속된 오버레이(호스트 hide 시 함께 hide).
        private List<UIPresenter> _linkedOverlays;
        internal IReadOnlyList<UIPresenter> LinkedOverlays => _linkedOverlays;
        internal void LinkOverlay(UIPresenter overlay) => (_linkedOverlays ??= new()).Add(overlay);
        internal void ClearLinkedOverlays() => _linkedOverlays?.Clear();

        // 뷰를 나중에 바인딩할 때 사용 (Host는 생성 시 미리 설정됨)
        internal void BindHost(IUIElementHost host) => Host = host;
        internal void BindView(UIView view) => ViewBase = view;

        internal void Subscribe(LifecycleEvent ev, Action<UIPresenter> handler)
        {
            if (handler == null) return;

            _subscribers ??= new();

            if (!_subscribers.TryGetValue(ev, out var list)) 
            { 
                list = new(); 
                _subscribers[ev] = list; 
            }

            list.Add(handler);
        }

        internal void Fire(LifecycleEvent ev)
        {
            if (_subscribers == null || !_subscribers.TryGetValue(ev, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                try { 
                    list[i](this); 
                }
                catch (Exception e) 
                { 
                    Debug.LogException(e); 
                }
            }
        }

        internal void SetTransitionOverride(IUITransition t) => TransitionOverride = t;

        // 라이프사이클 훅
        protected internal virtual void OnInitialize() { }
        protected internal virtual void OnBeforeShow() { }
        protected internal virtual void OnAfterShow() { }
        protected internal virtual void OnBeforeHide() { }
        protected internal virtual void OnAfterHide() { }

        // 커맨드
        public void Hide() => Host?.RequestHide(this);
    }

    public abstract class UIPresenter<TView> : UIPresenter where TView : UIView
    {
        protected TView View => (TView)ViewBase;
    }
}
