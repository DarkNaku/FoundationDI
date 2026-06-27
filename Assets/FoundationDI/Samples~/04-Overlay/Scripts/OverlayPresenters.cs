using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    [UIPrefab("HudAbove")]
    public class HudAboveOverlay : UIOverlayPresenter<HudOverlayView>   // Above(기본): Popup 위
    {
        private int _score;
        public void AddScore(int amount) { _score += amount; View.scoreLabel.text = $"Score: {_score}"; }
        protected override void OnInitialize() => View.scoreLabel.text = "Score: 0";
    }

    [UIPrefab("BackgroundBelow")]
    public class BackgroundBelowOverlay : UIOverlayPresenter<BackgroundOverlayView>
    {
        protected override bool Above => false;   // Below: Popup 아래(배경)
    }

    [UIPrefab("OverlayConfirm")]
    public class OverlayConfirm : UIPopupPresenter<OverlayConfirmView>
    {
        protected override void OnInitialize() => View.closeButton.onClick.AddListener(Hide);
    }

    [UIPrefab("OverlayHost")]
    public class OverlayHostPage : UIPagePresenter<OverlayHostView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
        {
            View.addScoreButton.onClick.AddListener(() => _ui.Overlay<HudAboveOverlay>().AddScore(10));
            View.popupButton.onClick.AddListener(() => _ui.Popup<OverlayConfirm>());
        }
    }

    public class OverlayDemo : IStartable
    {
        private readonly IUIManager _ui;
        public OverlayDemo(IUIManager ui) => _ui = ui;

        public void Start()
        {
            _ui.Overlay<BackgroundBelowOverlay>();   // Below 배경
            _ui.Page<OverlayHostPage>();              // Page
            _ui.Overlay<HudAboveOverlay>();           // Above HUD
        }
    }
}
