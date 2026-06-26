using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPresenterBase
    {
        public enum LifecycleEvent { BeforeShow, AfterShow, BeforeHide, AfterHide, Destroyed }

        internal UIView ViewBase { get; private set; }
        internal IUIElementHost Host { get; private set; }
        internal IUITransition TransitionOverride { get; private set; }

        private Dictionary<LifecycleEvent, List<Action<UIPresenterBase>>> _subscribers;

        internal void Bind(UIView view, IUIElementHost host) 
        { 
            ViewBase = view; 
            Host = host; 
        }

        internal void Subscribe(LifecycleEvent ev, Action<UIPresenterBase> handler)
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

        internal virtual void ResetTransient()
        {
            _subscribers?.Clear();
            TransitionOverride = null;
        }

        // 라이프사이클 훅
        protected internal virtual void OnInitialize() { }
        protected internal virtual void OnBeforeShow() { }
        protected internal virtual void OnAfterShow() { }
        protected internal virtual void OnBeforeHide() { }
        protected internal virtual void OnAfterHide() { }
        protected internal virtual void OnDestroyElement() { }

        // 커맨드
        public void Hide() => Host?.RequestHide(this);
    }

    public abstract class UIPresenterBase<TView> : UIPresenterBase where TView : UIView
    {
        protected TView View => (TView)ViewBase;
    }
}
