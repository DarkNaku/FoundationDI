using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPagePresenter<TView> : UIPresenter<TView> where TView : UIView
    {
        public UIPagePresenter<TView> OnBeforeShow(Action<UIPagePresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.BeforeShow, p => cb((UIPagePresenter<TView>)p));
            return this;
        }

        public UIPagePresenter<TView> OnAfterShow(Action<UIPagePresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.AfterShow, p => cb((UIPagePresenter<TView>)p));
            return this;
        }

        public UIPagePresenter<TView> OnBeforeHide(Action<UIPagePresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.BeforeHide, p => cb((UIPagePresenter<TView>)p));
            return this;
        }

        public UIPagePresenter<TView> OnAfterHide(Action<UIPagePresenter<TView>> cb)
        {
            Subscribe(LifecycleEvent.AfterHide, p => cb((UIPagePresenter<TView>)p));
            return this;
        }

        public UIPagePresenter<TView> WithTransition(IUITransition transition) 
        { 
            SetTransitionOverride(transition); 
            return this; 
        }

        public UIPagePresenter<TView> With<TParams>(TParams p)
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
