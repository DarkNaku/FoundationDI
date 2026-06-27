using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    [UIPrefab("MainMenu")]
    public class MainMenuPage : UIPagePresenter<MainMenuView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
        {
            View.openPopupButton.onClick.AddListener(() => _ui.Popup<SettingsPopup>());
            View.openOverlayButton.onClick.AddListener(() => _ui.Overlay<HudOverlay>());
        }
    }

    [UIPrefab("SettingsPopup")]
    public class SettingsPopup : UIPopupPresenter<SettingsView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
        {
            // 서로 다른 타입(ConfirmPopup)을 스택에 쌓아 LIFO 시연(같은 타입은 중복 무시됨)
            View.openConfirmButton.onClick.AddListener(() => _ui.Popup<ConfirmPopup>());
            View.closeButton.onClick.AddListener(Hide);
        }
    }

    [UIPrefab("ConfirmPopup")]
    public class ConfirmPopup : UIPopupPresenter<ConfirmView>
    {
        protected override void OnInitialize()
            => View.closeButton.onClick.AddListener(Hide);
    }

    [UIPrefab("Hud")]
    public class HudOverlay : UIOverlayPresenter<HudView>
    {
        protected override void OnInitialize() => View.label.text = "HUD (Above)";
    }

    public class BasicUsageDemo : IStartable
    {
        private readonly IUIManager _ui;
        public BasicUsageDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<MainMenuPage>();
    }
}
