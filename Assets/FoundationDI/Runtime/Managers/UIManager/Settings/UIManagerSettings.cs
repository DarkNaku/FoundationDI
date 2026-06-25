using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private UITransitionAsset _defaultPageTransition;
        [SerializeField] private UITransitionAsset _defaultPopupTransition;
        [SerializeField] private UITransitionAsset _defaultOverlayTransition;

        public IUITransition DefaultPageTransition => _defaultPageTransition;
        public IUITransition DefaultPopupTransition => _defaultPopupTransition;
        public IUITransition DefaultOverlayTransition => _defaultOverlayTransition;
    }
}
