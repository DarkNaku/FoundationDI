using System;

namespace DarkNaku.FoundationDI
{
    public abstract class UIOverlayPresenter<TView> : UIPresenterBase<TView>, IOverlayPlacement where TView : UIView
    {
        protected internal virtual bool Above => true;
        bool IOverlayPlacement.Above => Above;

        public UIOverlayPresenter<TView> OnShown(Action<UIOverlayPresenter<TView>> cb)
        { Subscribe(LifecycleEvent.Shown, p => cb((UIOverlayPresenter<TView>)p)); return this; }

        public UIOverlayPresenter<TView> OnAfterHidden(Action<UIOverlayPresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterHidden, p => cb((UIOverlayPresenter<TView>)p)); return this; }

        public UIOverlayPresenter<TView> WithTransition(IUITransition t) { SetTransitionOverride(t); return this; }

        public UIOverlayPresenter<TView> With<TParams>(TParams p)
        { if (this is IConfigurable<TParams> c) c.Configure(p); return this; }
    }
}
