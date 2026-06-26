using System;
using UnityEngine;

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
        {
            if (this is IConfigurable<TParams> c) c.Configure(p);
            else Debug.LogWarning($"[UIManager] {GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 With(...)가 무시됩니다.");
            return this;
        }
    }
}
