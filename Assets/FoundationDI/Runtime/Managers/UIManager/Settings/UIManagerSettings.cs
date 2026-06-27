using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private UITransitionAsset _defaultPageTransition;
        [SerializeField] private UITransitionAsset _defaultPopupTransition;
        [SerializeField] private UITransitionAsset _defaultOverlayTransition;

        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        public IUITransition DefaultPageTransition => _defaultPageTransition;
        public IUITransition DefaultPopupTransition => _defaultPopupTransition;
        public IUITransition DefaultOverlayTransition => _defaultOverlayTransition;

        // CanvasScaler(Scale With Screen Size, Expand)의 기준 해상도
        public Vector2 ReferenceResolution => _referenceResolution;
    }
}
