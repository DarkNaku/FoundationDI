using System;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IObjectResolver _resolver;
        private readonly IUIAssetLoader _loader;

        public UIInstanceFactory(IObjectResolver resolver, IUIAssetLoader loader)
        {
            _resolver = resolver;
            _loader = loader;
        }

        public UIPresenterBase Create(Type presenterType, IUIElementHost host)
        {
            var key = UIPrefabKeyResolver.Resolve(presenterType);
            var prefab = _loader.Load(key);

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
