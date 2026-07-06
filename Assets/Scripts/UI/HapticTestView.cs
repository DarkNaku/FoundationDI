using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// HapticService의 모든 API를 시연/검증하는 페이지.
    public class HapticTestView : UIView
    {
        [Header("Impact")]
        [SerializeField] public Button lightButton;
        [SerializeField] public Button mediumButton;
        [SerializeField] public Button heavyButton;
        [SerializeField] public Button softButton;
        [SerializeField] public Button rigidButton;

        [Header("Notification")]
        [SerializeField] public Button successButton;
        [SerializeField] public Button warningButton;
        [SerializeField] public Button errorButton;

        [Header("Selection")]
        [SerializeField] public Button selectionButton;

        [Header("Enabled 토글")]
        [SerializeField] public Button enabledButton;
        [SerializeField] public Text enabledLabel;

        [Header("상태 / 네비게이션")]
        [SerializeField] public Text statusLabel;
        [SerializeField] public Button backButton;
    }
}
