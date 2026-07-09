using System;
using VContainer;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IObjectResolver _resolver;

        public UIInstanceFactory(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        // View는 풀이 제공한다. 여기서는 presenter만 생성/주입/바인딩한다.
        public UIPresenter CreatePresenter(Type presenterType, UIView view, IUIElementHost host)
        {
            var presenter = (UIPresenter)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);
            presenter.Bind(view, host);
            return presenter;
        }

        // Host만 미리 설정하고 View 바인딩은 나중에 (BindView) 한다.
        // UIManager 내부에서 Pool.Get 전에 presenter를 반환해야 할 때 사용.
        internal UIPresenter CreatePresenterWithHost(Type presenterType, IUIElementHost host)
        {
            var presenter = (UIPresenter)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);
            presenter.BindHost(host);
            return presenter;
        }
    }
}
