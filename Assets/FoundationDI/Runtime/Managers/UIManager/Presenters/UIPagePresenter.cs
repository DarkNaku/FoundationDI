using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPagePresenter<TView> : UIPresenterBase<TView> where TView : UIView
    {
        public UIPagePresenter<TView> OnShown(Action<UIPagePresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterShow, p => cb((UIPagePresenter<TView>)p)); return this; }

        public UIPagePresenter<TView> OnAfterHidden(Action<UIPagePresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterHide, p => cb((UIPagePresenter<TView>)p)); return this; }

        public UIPagePresenter<TView> WithTransition(IUITransition t) { SetTransitionOverride(t); return this; }

        public UIPagePresenter<TView> With<TParams>(TParams p)
        {
            if (this is IConfigurable<TParams> c) c.Configure(p);
            else Debug.LogWarning($"[UIManager] {GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 With(...)가 무시됩니다.");
            return this;
        }
    }
}
