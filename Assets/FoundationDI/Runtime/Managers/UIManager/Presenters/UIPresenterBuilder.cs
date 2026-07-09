using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 빌더 체이닝을 위한 공통 기반. TSelf(카테고리 타입)를 반환해 .OnAfterShow(...).WithTransition(...)
    // 같은 구체 타입 체인을 유지한다. Page/Popup/Overlay는 이 클래스를 상속만 하면 된다.
    public abstract class UIPresenterBuilder<TSelf, TView> : UIPresenter<TView>
        where TSelf : UIPresenterBuilder<TSelf, TView>
        where TView : UIView
    {
        public TSelf OnBeforeShow(Action<TSelf> cb) { Subscribe(LifecycleEvent.BeforeShow, p => cb((TSelf)p)); return (TSelf)this; }
        public TSelf OnAfterShow(Action<TSelf> cb) { Subscribe(LifecycleEvent.AfterShow, p => cb((TSelf)p)); return (TSelf)this; }
        public TSelf OnBeforeHide(Action<TSelf> cb) { Subscribe(LifecycleEvent.BeforeHide, p => cb((TSelf)p)); return (TSelf)this; }
        public TSelf OnAfterHide(Action<TSelf> cb) { Subscribe(LifecycleEvent.AfterHide, p => cb((TSelf)p)); return (TSelf)this; }

        public TSelf WithTransition(IUITransition transition)
        {
            SetTransitionOverride(transition);
            return (TSelf)this;
        }

        /// <summary>
        /// Presenter가 <see cref="IConfigurable{TParams}"/>를 구현한 경우 <c>Configure(p)</c>를 동기 호출한다.
        /// </summary>
        /// <remarks>
        /// <b>주의:</b> <c>Configure</c>는 View에 접근하지 말 것 — 호출 시점에 View가 아직 바인딩되지 않았을 수 있다.
        /// 전달 params만 저장하고 View 접근은 <c>OnInitialize</c>/<c>OnBeforeShow</c>에서 수행한다.
        /// </remarks>
        public TSelf With<TParams>(TParams p)
        {
            if (this is IConfigurable<TParams> config)
            {
                config.Configure(p);
            }
            else
            {
                Debug.LogWarning($"[UIManager] {GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 With(...)가 무시됩니다.");
            }

            return (TSelf)this;
        }
    }
}
