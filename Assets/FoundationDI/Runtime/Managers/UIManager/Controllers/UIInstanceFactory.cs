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

        // Host만 미리 설정하고 View 바인딩은 나중에 (BindView) 한다.
        // UIManager 내부에서 Pool.Get 전에 presenter를 반환해야 할 때 사용.
        internal UIPresenter CreatePresenter(Type presenterType, IUIElementHost host)
        {
            var presenter = (UIPresenter)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);
            presenter.BindHost(host);
            return presenter;
        }
    }
}
