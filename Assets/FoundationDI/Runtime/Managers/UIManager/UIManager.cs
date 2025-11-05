using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace FoundationDI 
{
    public interface IUIManager
    {
        void ChangeView(string pageName);
        T ShowPopup<T>(string popupName) where T : class, IUIPresenter;
        IUIPresenter ShowPopup(string popupName);
        void HidePopup(IUIPresenter presenter);
    }
    
    public class UIManager : IUIManager
    {
        private readonly IObjectResolver _container;
        private readonly IUISetting _uiSetting;
        private readonly Dictionary<string, IUIPresenter> _instances = new Dictionary<string, IUIPresenter>();
        private readonly List<IUIPresenter> _popups = new List<IUIPresenter>();

        private IUIPresenter _currentPage;
        
        public UIManager(IObjectResolver container, IUISetting uiSetting)
        {
            _container = container;
            _uiSetting = uiSetting;
        }

        public void ChangeView(string pageName) => AsyncChangeTo(pageName).Forget();

        public IUIPresenter ShowPopup(string popupName) => ShowPopup<IUIPresenter>(popupName);

        public T ShowPopup<T>(string popupName) where T : class, IUIPresenter
        {
            try
            {
                var presenter = GetUI(popupName);

                if (presenter == null) return null;

                if (_popups.Count > 0)
                {
                    _popups[^1].InputEnabled = false;
                }
                else
                {
                    if (_currentPage != null)
                    {
                        _currentPage.InputEnabled = false;
                    }
                }
                
                _popups.Add(presenter);
                
                AsyncShowPopup(presenter).Forget();

                return presenter as T;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            
            return null;
        }

        public void HidePopup(IUIPresenter presenter) => AsyncHidePopup(presenter).Forget();
        
        private async UniTask AsyncChangeTo(string pageName)
        {
            try
            {
                await UniTask.NextFrame();
                
                _currentPage?.Hide();

                _currentPage = GetUI(pageName);

                if (_currentPage == null) return;

                await _currentPage.Show();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async UniTask AsyncShowPopup(IUIPresenter presenter)
        {
            try
            {
                await UniTask.NextFrame();
                await presenter.Show();

                presenter.InputEnabled = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        
        private async UniTask AsyncHidePopup(IUIPresenter presenter)
        {
            try
            {
                if (_popups.Count == 0) return;
                if (_popups[^1] != presenter) return;
                
                presenter.InputEnabled = false;

                await presenter.Hide();
                
                _popups.RemoveAt(_popups.Count - 1);

                if (_popups.Count > 0)
                {
                    _popups[^1].InputEnabled = true;
                }
                else
                {
                    if (_currentPage != null) _currentPage.InputEnabled = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private IUIPresenter GetUI(string uiName)
        {
            if (_instances.TryGetValue(uiName, out var presenter) == false)
            {
                presenter = CreateUI(uiName);
                presenter.Initialize();
            }

            return presenter;
        }

        private IUIPresenter CreateUI(string uiName)
        {
            var entry = _uiSetting.Find(uiName);

            if (entry == null)
            {
                Debug.LogError($"[UIManager] CreateUI : Cannot found entry - {uiName}");
                return null;
            }

            var view = _container.Instantiate(entry.Prefab);
            var type = entry.PresenterType;
            var presenter = Activator.CreateInstance(type, this, view) as IUIPresenter;

            if (presenter == null)
            {
                Debug.LogError($"[UIManager] CreateUI : Fail to create presenter - {uiName}");
                return null;
            }

            _container.Inject(presenter);
            
            _instances[uiName] = presenter;

            return presenter;
        }
    }
}