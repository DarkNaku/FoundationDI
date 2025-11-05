using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FoundationDI
{
    public interface IUIPresenter
    {
        bool InputEnabled { get; set; }
        void Initialize();
        UniTask Show();
        UniTask Hide();
        void Close();
        U OnBeforeHide<U>(System.Action<U> onBeforeHide) where U : class, IUIPresenter;
        U OnAfterHide<U>(System.Action<U> onAfterHide) where U : class, IUIPresenter;
    }
    
    public abstract class UIPresenter<T> : IUIPresenter where T : class, IUIView
    {
        public bool InputEnabled
        {
            get => View.InputEnabled; 
            set => View.InputEnabled = value;
        }
        
        protected IUIManager Manager { get; }
        protected T View { get; }
        
        private System.Action<IUIPresenter> _onBeforeHide;
        private System.Action<IUIPresenter> _onAfterHide;

        protected UIPresenter(IUIManager pageManager, T view)
        {
            Manager = pageManager;
            View = view;
        }

        public void Initialize()
        {
            OnInitialize();
            View.Initialize();
        }
        
        public async UniTask Show()
        {
            OnEnter();
            await View.Show();
        }

        public async UniTask Hide()
        {
            _onBeforeHide?.Invoke(this);
            await View.Hide();
            OnExit();
            _onAfterHide?.Invoke(this);
            
            _onBeforeHide = null;
            _onAfterHide = null;
        }

        public void Close() => Manager.HidePopup(this);

        public U OnBeforeHide<U>(System.Action<U> onBeforeHide) where U : class, IUIPresenter
        {
            _onBeforeHide = presenter => onBeforeHide?.Invoke(presenter as U);
            
            return this as U;
        }
        
        public U OnAfterHide<U>(System.Action<U> onAfterHide) where U : class, IUIPresenter
        {
            _onAfterHide = presenter => onAfterHide?.Invoke(presenter as U);
            
            return this as U;
        }

        protected virtual void OnInitialize() { }

        protected virtual void OnEnter() { }

        protected virtual void OnExit() { }
    }
}
