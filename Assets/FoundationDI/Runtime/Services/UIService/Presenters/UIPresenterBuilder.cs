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
        /// 이 Page/Popup을 표시할 때 오버레이 <typeparamref name="TOverlay"/>를 함께 노출한다.
        /// 오버레이는 호스트와 동시에 애니메이션되며(호스트 트랜지션 오버라이드를 공유), 호스트가
        /// 숨겨지면 함께 숨겨진다.
        /// </summary>
        /// <param name="persistent">
        /// true면 페이지 전환 시 <b>다음 페이지도 같은 타입을 persistent로 요청</b>하면 teardown 없이
        /// 그 페이지로 소유권이 이전되어 연속 유지된다(깜빡임 없음). 다음 페이지가 요청하지 않으면 정상 hide.
        /// 기본값 false면 호스트별 인스턴스(전환 시 hide 후 재생성).
        /// </param>
        /// <param name="configure">
        /// View 바인딩/OnInitialize 전에 호출되므로 파라미터 저장 용도로만 쓰고 View에 접근하지 말 것.
        /// (persistent 이전으로 재사용되는 경우엔 호출되지 않는다 — 이미 초기화된 인스턴스이므로.)
        /// </param>
        public TSelf WithOverlay<TOverlay>(bool persistent = false, Action<TOverlay> configure = null) where TOverlay : UIPresenter
        {
            AddOverlayRequest(typeof(TOverlay), persistent,
                configure == null ? null : new Action<UIPresenter>(p => configure((TOverlay)p)));
            return (TSelf)this;
        }

        /// <summary>
        /// Presenter가 <see cref="IConfigurable{TParams}"/>를 구현한 경우 <c>Configure(p)</c>를 동기 호출한다.
        /// </summary>
        /// <remarks>
        /// <b>주의:</b> <c>Configure</c>는 View에 접근하지 말 것 — 호출 시점에 View가 아직 바인딩되지 않았을 수 있다.
        /// 전달 params만 저장하고 View 접근은 <c>OnInitialize</c>/<c>OnBeforeShow</c>에서 수행한다.
        /// </remarks>
        public TSelf WithParams<TParams>(TParams p)
        {
            if (this is IConfigurable<TParams> config)
            {
                config.Configure(p);
            }
            else
            {
                Debug.LogWarning($"[UIService] {GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 WithParams(...)가 무시됩니다.");
            }

            return (TSelf)this;
        }
    }
}
