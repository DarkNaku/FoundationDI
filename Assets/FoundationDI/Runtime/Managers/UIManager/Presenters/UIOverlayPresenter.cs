using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIOverlayPresenter<TView> : UIPresenterBase<TView>, IOverlayPlacement where TView : UIView
    {
        protected internal virtual bool Above => true;
        bool IOverlayPlacement.Above => Above;

        public UIOverlayPresenter<TView> OnShown(Action<UIOverlayPresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterShown, p => cb((UIOverlayPresenter<TView>)p)); return this; }

        public UIOverlayPresenter<TView> OnAfterHidden(Action<UIOverlayPresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterHidden, p => cb((UIOverlayPresenter<TView>)p)); return this; }

        public UIOverlayPresenter<TView> WithTransition(IUITransition t) { SetTransitionOverride(t); return this; }

        public UIOverlayPresenter<TView> With<TParams>(TParams p)
        {
            if (this is IConfigurable<TParams> c) c.Configure(p);
            else Debug.LogWarning($"[UIManager] {GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 With(...)가 무시됩니다.");
            return this;
        }
    }
}
