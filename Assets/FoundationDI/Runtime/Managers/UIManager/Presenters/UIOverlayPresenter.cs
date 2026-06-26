using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIOverlayPresenter<TView> : UIPresenter<TView>, IOverlayPlacement where TView : UIView
    {
        protected internal virtual bool Above => true;
        bool IOverlayPlacement.Above => Above;

        public UIOverlayPresenter<TView> OnBeforeShow(Action<UIOverlayPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.BeforeShow, p => cb((UIOverlayPresenter<TView>)p));
            return this;
        }

        public UIOverlayPresenter<TView> OnAfterShow(Action<UIOverlayPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.AfterShow, p => cb((UIOverlayPresenter<TView>)p));
            return this;
        }

        public UIOverlayPresenter<TView> OnBeforeHide(Action<UIOverlayPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.BeforeHide, p => cb((UIOverlayPresenter<TView>)p));
            return this;
        }

        public UIOverlayPresenter<TView> OnAfterHide(Action<UIOverlayPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.AfterHide, p => cb((UIOverlayPresenter<TView>)p));
            return this;
        }

        public UIOverlayPresenter<TView> WithTransition(IUITransition t) 
        { 
            SetTransitionOverride(t); 
            return this; 
        }

        public UIOverlayPresenter<TView> With<TParams>(TParams p)
        {
            if (this is IConfigurable<TParams> config) 
            {
                config.Configure(p);
            } 
            else 
            {
                Debug.LogWarning($"[UIManager] {GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 With(...)가 무시됩니다.");
            }

            return this;
        }
    }
}
