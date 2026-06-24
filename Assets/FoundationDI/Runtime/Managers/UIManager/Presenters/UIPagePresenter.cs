using System;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPagePresenter<TView> : UIPresenterBase<TView> where TView : UIView
    {
        public UIPagePresenter<TView> OnShown(Action<UIPagePresenter<TView>> cb)
        { Subscribe(LifecycleEvent.Shown, p => cb((UIPagePresenter<TView>)p)); return this; }

        public UIPagePresenter<TView> OnAfterHidden(Action<UIPagePresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterHidden, p => cb((UIPagePresenter<TView>)p)); return this; }

        public UIPagePresenter<TView> WithTransition(IUITransition t) { SetTransitionOverride(t); return this; }

        public UIPagePresenter<TView> With<TParams>(TParams p)
        { if (this is IConfigurable<TParams> c) c.Configure(p); return this; }
    }
}
