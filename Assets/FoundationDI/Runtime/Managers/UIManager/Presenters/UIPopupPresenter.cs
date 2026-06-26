using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPopupPresenter<TView> : UIPresenter<TView> where TView : UIView
    {
        public UIPopupPresenter<TView> OnBeforeShow(Action<UIPopupPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.BeforeShow, p => cb((UIPopupPresenter<TView>)p));
            return this;
        }

        public UIPopupPresenter<TView> OnAfterShow(Action<UIPopupPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.AfterShow, p => cb((UIPopupPresenter<TView>)p));
            return this;
        }

        public UIPopupPresenter<TView> OnBeforeHide(Action<UIPopupPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.BeforeHide, p => cb((UIPopupPresenter<TView>)p));
            return this;
        }

        public UIPopupPresenter<TView> OnAfterHide(Action<UIPopupPresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.AfterHide, p => cb((UIPopupPresenter<TView>)p));
            return this;
        }

        public UIPopupPresenter<TView> WithTransition(IUITransition transition) 
        { 
            SetTransitionOverride(transition); 
            return this; 
        }

        public UIPopupPresenter<TView> With<TParams>(TParams p)
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
