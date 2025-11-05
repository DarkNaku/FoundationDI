using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace FoundationDI
{
    public interface IUIView
    {
        bool InputEnabled { get; set; }
        void Initialize();
        UniTask Show();
        UniTask Hide();
    }
    
    public abstract class UIView : MonoBehaviour, IUIView
    {
        public bool InputEnabled
        {
            get
            {
                if (GR == null) return false;
                    
                return _graphicRaycaster.enabled;   
            }
            set
            {
                if (GR != null) _graphicRaycaster.enabled = value;   
            }
        }

        private GraphicRaycaster GR => _graphicRaycaster ??= GetComponent<GraphicRaycaster>();
        private GraphicRaycaster _graphicRaycaster;
        
        public virtual void Initialize() { }
        
        public async UniTask Show()
        {
            gameObject.SetActive(true);
            OnEnterBefore();
            await TransitionIn();
            OnEnterAfter();
        }

        public async UniTask Hide()
        {
            OnExitBefore();
            await TransitionOut();
            OnExitAfter();
            gameObject.SetActive(false);
        }
        
        protected virtual UniTask TransitionIn() => UniTask.CompletedTask;
        protected virtual UniTask TransitionOut() => UniTask.CompletedTask;
        protected virtual void OnEnterBefore() { }
        protected virtual void OnEnterAfter() { }
        protected virtual void OnExitBefore() { }
        protected virtual void OnExitAfter() { }
    }
}