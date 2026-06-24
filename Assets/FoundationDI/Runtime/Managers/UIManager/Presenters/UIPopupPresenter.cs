using System;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPopupPresenter<TView> : UIPresenterBase<TView> where TView : UIView
    {
        public UIPopupPresenter<TView> OnShown(Action<UIPopupPresenter<TView>> cb)
        { Subscribe(LifecycleEvent.Shown, p => cb((UIPopupPresenter<TView>)p)); return this; }

        public UIPopupPresenter<TView> OnAfterHidden(Action<UIPopupPresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterHidden, p => cb((UIPopupPresenter<TView>)p)); return this; }

        public UIPopupPresenter<TView> WithTransition(IUITransition t) { SetTransitionOverride(t); return this; }

        public UIPopupPresenter<TView> With<TParams>(TParams p)
        { if (this is IConfigurable<TParams> c) c.Configure(p); return this; }
    }
}
