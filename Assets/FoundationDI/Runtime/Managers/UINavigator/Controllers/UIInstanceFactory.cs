using System;
using VContainer;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IObjectResolver _resolver;

        // UINavigator 전용 풀도 같은 컨테이너로 View 계층을 주입해야 하므로 노출한다.
        internal IObjectResolver Resolver => _resolver;

        public UIInstanceFactory(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        // Host만 미리 설정하고 View 바인딩은 나중에 (BindView) 한다.
        // UINavigator 내부에서 Pool.Get 전에 presenter를 반환해야 할 때 사용.
        internal UIPresenter CreatePresenter(Type presenterType, IUIElementHost host)
        {
            var presenter = (UIPresenter)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);
            presenter.BindHost(host);
            return presenter;
        }
    }
}
