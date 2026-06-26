using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIView : MonoBehaviour
    {
        [SerializeField] private UITransitionAsset _transition;

        private static readonly NoopTransition Noop = new();

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= (RectTransform)transform;

        private CanvasGroup _canvasGroup;
        private CanvasGroup CanvasGroup => _canvasGroup ??= GetComponent<CanvasGroup>();

        public bool InputEnabled
        {
            get => CanvasGroup.interactable;
            set => CanvasGroup.interactable = value;
        }

        // 해석 우선순위: per-show 오버라이드(Transition) > 인스펙터 에셋(_transition) > settings 모드 기본값(DefaultTransition) > Noop
        // 하나의 IUITransition이 ShowAsync/HideAsync 한 쌍을 정의하므로 show/hide 슬롯을 분리하지 않는다.
        public IUITransition Transition { get; set; }
        public IUITransition DefaultTransition { get; set; }

        private IUITransition Resolve() => Transition ?? _transition ?? DefaultTransition ?? Noop;

        public virtual void OnInitializeView() { }

        public UniTask PlayShow(CancellationToken ct) => Resolve().ShowAsync(RectTransform, ct);
        public UniTask PlayHide(CancellationToken ct) => Resolve().HideAsync(RectTransform, ct);
    }
}
