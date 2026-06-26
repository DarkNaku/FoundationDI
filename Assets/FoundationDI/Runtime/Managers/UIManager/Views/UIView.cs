using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    public abstract class UIView : MonoBehaviour
    {
        [SerializeField] private UITransitionAsset _showTransition;
        [SerializeField] private UITransitionAsset _hideTransition;

        private static readonly NoopTransition Noop = new();

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= (RectTransform)transform;

        private GraphicRaycaster _raycaster;
        private GraphicRaycaster Raycaster => _raycaster ??= GetComponent<GraphicRaycaster>();

        public bool InputEnabled
        {
            get => Raycaster != null && Raycaster.enabled;
            set { if (Raycaster != null) Raycaster.enabled = value; }
        }

        // 우선순위: per-show 오버라이드 > 인스펙터 에셋 > settings 모드 기본값 > Noop
        public IUITransition ShowTransition { get; set; }
        public IUITransition HideTransition { get; set; }
        public IUITransition DefaultTransition { get; set; }

        public virtual void OnInitializeView() { }

        public UniTask PlayShow(CancellationToken ct)
            => (ShowTransition ?? _showTransition ?? DefaultTransition ?? (IUITransition)Noop).PlayShow(RectTransform, ct);

        public UniTask PlayHide(CancellationToken ct)
            => (HideTransition ?? _hideTransition ?? DefaultTransition ?? (IUITransition)Noop).PlayHide(RectTransform, ct);
    }
}
