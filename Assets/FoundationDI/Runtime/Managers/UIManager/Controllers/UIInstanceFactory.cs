using System;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IObjectResolver _resolver;
        private readonly IResourceService _resource;

        public UIInstanceFactory(IObjectResolver resolver, IResourceService resource)
        {
            _resolver = resolver;
            _resource = resource;
        }

        public UIPresenterBase Create(Type presenterType, IUIElementHost host)
        {
            var key = UIPrefabKeyResolver.Resolve(presenterType);
            var prefab = _resource.Load<GameObject>(key);

            var go = UnityEngine.Object.Instantiate(prefab);
            var view = go.GetComponent<UIView>();
            if (view == null)
            {
                UnityEngine.Object.Destroy(go);
                throw new InvalidOperationException($"[UIManager] {key} prefab 루트에 UIView가 없습니다.");
            }

            var presenter = (UIPresenterBase)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);

            presenter.Bind(view, host);
            view.OnInitializeView();
            presenter.OnInitialize();

            return presenter;
        }
    }
}
