using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIView : MonoBehaviour, IPoolItem
    {
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

        // 해석 우선순위: per-show 오버라이드(Transition) > 부착된 트랜지션 컴포넌트 > Noop
        public IUITransition Transition { get; set; }

        private IUITransition _componentTransition;
        private bool _resolvedComponent;

        private IUITransition ResolveComponent()
        {
            if (!_resolvedComponent)
            {
                _componentTransition = GetComponent<IUITransition>();
                _resolvedComponent = true;
            }
            return _componentTransition;
        }

        private IUITransition Resolve() => Transition ?? ResolveComponent() ?? Noop;

        // 뷰 자체 초기화(물리 인스턴스당 1회) / 파괴 훅. presenter 존재와 무관.
        public virtual void OnInitializeView() { }
        protected virtual void OnDestroyView() { }

        public UniTask ShowAsync(CancellationToken ct) => Resolve().ShowAsync(RectTransform, ct);
        public UniTask HideAsync(CancellationToken ct) => Resolve().HideAsync(RectTransform, ct);

        // === IPoolItem (풀 세부는 명시적 구현으로 감춘다) ===
        GameObject IPoolItem.GO => this != null ? gameObject : null;
        PoolData IPoolItem.PD { get; set; }

        void IPoolItem.OnCreateItem()
        {
            OnInitializeView();
            gameObject.SetActive(false); // 풀 상주 중 비활성
        }

        void IPoolItem.OnGetItem() { } // 활성화는 UIManager show 흐름이 제어

        void IPoolItem.OnReleaseItem()
        {
            if (this == null) return;
            if (gameObject != null) gameObject.SetActive(false);
        }

        void IPoolItem.OnDestroyItem() => OnDestroyView();

        // UI 풀은 지연 반환을 쓰지 않는다(delay 무시).
        void IPoolItem.Release(float delay) => ((IPoolItem)this).PD?.Release(this);
    }
}
