using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Page/Popup/Overlay가 공유하는 기반. 빌더 체인 자체는 아래 UIPresenterExtensions의
    // 확장 메서드가 제공하므로, 이 클래스에는 체인에 낄 수 없는 WithOverlay만 남는다.
    public abstract class UIPresenterBuilder<TView> : UIPresenter<TView> where TView : UIView
    {
        /// <summary>
        /// 이 Page/Popup을 표시할 때 오버레이 <typeparamref name="TOverlay"/>를 함께 노출한다.
        /// 오버레이는 호스트와 동시에 애니메이션되며(호스트 트랜지션 오버라이드를 공유), 호스트가
        /// 숨겨지면 함께 숨겨진다.
        /// </summary>
        /// <remarks>
        /// 체인에 끼지 않고 문(statement)으로 쓴다. 확장 메서드로 만들면 타입 인자가 둘
        /// (수신자 타입, <typeparamref name="TOverlay"/>)이 되는데, C#은 일부만 명시하고 나머지를
        /// 추론할 수 없어 <c>WithOverlay&lt;Dim&gt;()</c> 호출이 성립하지 않는다. 그래서 인스턴스
        /// 메서드로 남기되, 체인 중간에서 구체 타입이 소실되지 않도록 반환값을 두지 않는다.
        /// </remarks>
        /// <param name="persistent">
        /// true면 페이지 전환 시 <b>다음 페이지도 같은 타입을 persistent로 요청</b>하면 teardown 없이
        /// 그 페이지로 소유권이 이전되어 연속 유지된다(깜빡임 없음). 다음 페이지가 요청하지 않으면 정상 hide.
        /// 기본값 false면 호스트별 인스턴스(전환 시 hide 후 재생성).
        /// </param>
        /// <param name="configure">
        /// View 바인딩/OnInitialize 전에 호출되므로 파라미터 저장 용도로만 쓰고 View에 접근하지 말 것.
        /// (persistent 이전으로 재사용되는 경우엔 호출되지 않는다 — 이미 초기화된 인스턴스이므로.)
        /// </param>
        public void WithOverlay<TOverlay>(bool persistent = false, Action<TOverlay> configure = null) where TOverlay : UIPresenter
        {
            AddOverlayRequest(typeof(TOverlay), persistent,
                configure == null ? null : new Action<UIPresenter>(p => configure((TOverlay)p)));
        }
    }

    /// <summary>
    /// 빌더 체인. 확장 메서드라 수신자의 <b>구체 타입</b>이 <typeparamref name="T"/>로 추론되므로,
    /// 콜백 파라미터와 체인 반환이 모두 사용자가 선언한 Presenter 타입 그대로다.
    /// </summary>
    /// <remarks>
    /// 제약이 <c>UIPresenter</c>인 것은 취향이 아니라 강제다. <c>UIPresenterBuilder&lt;TView&gt;</c>로
    /// 조이면 타입 인자가 둘이 되어 추론이 깨진다.
    /// </remarks>
    public static class UIPresenterExtensions
    {
        public static T OnBeforeShow<T>(this T self, Action<T> cb) where T : UIPresenter
            => Listen(self, UIPresenter.LifecycleEvent.BeforeShow, cb);

        public static T OnAfterShow<T>(this T self, Action<T> cb) where T : UIPresenter
            => Listen(self, UIPresenter.LifecycleEvent.AfterShow, cb);

        public static T OnBeforeHide<T>(this T self, Action<T> cb) where T : UIPresenter
            => Listen(self, UIPresenter.LifecycleEvent.BeforeHide, cb);

        public static T OnAfterHide<T>(this T self, Action<T> cb) where T : UIPresenter
            => Listen(self, UIPresenter.LifecycleEvent.AfterHide, cb);

        public static T WithTransition<T>(this T self, IUITransition transition) where T : UIPresenter
        {
            self.SetTransitionOverride(transition);
            return self;
        }

        /// <summary>
        /// Presenter가 <see cref="IConfigurable{TParams}"/>를 구현한 경우 <c>Configure(p)</c>를 동기 호출한다.
        /// </summary>
        /// <remarks>
        /// <b>주의:</b> <c>Configure</c>는 View에 접근하지 말 것 — 호출 시점에 View가 아직 바인딩되지 않았을 수 있다.
        /// 전달 params만 저장하고 View 접근은 <c>OnInitialize</c>/<c>OnBeforeShow</c>에서 수행한다.
        /// </remarks>
        public static T WithParams<T, TParams>(this T self, TParams p) where T : UIPresenter
        {
            if (self is IConfigurable<TParams> config)
            {
                config.Configure(p);
            }
            else
            {
                Debug.LogWarning($"[UINavigator] {self.GetType().Name}이(가) IConfigurable<{typeof(TParams).Name}>를 구현하지 않아 WithParams(...)가 무시됩니다.");
            }

            return self;
        }

        // Fire가 핸들러에 넘기는 것은 언제나 self 자신이므로 (T) 캐스트는 항상 성립한다.
        private static T Listen<T>(T self, UIPresenter.LifecycleEvent ev, Action<T> cb) where T : UIPresenter
        {
            self.Subscribe(ev, p => cb((T)p));
            return self;
        }
    }
}
