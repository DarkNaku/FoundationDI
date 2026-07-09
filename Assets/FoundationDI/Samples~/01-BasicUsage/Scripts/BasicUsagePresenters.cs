using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    // View는 풀에서 재사용되고 Presenter는 매 Show마다 새로 생성된다.
    // 따라서 버튼 구독은 OnBeforeShow에서 걸고 OnAfterHide에서 해제한다.
    // (OnInitialize/OnBeforeShow에서 걸고 해제하지 않으면 재표시 때 중복 핸들러가 쌓인다.)
    [UIPrefab("MainMenu")]
    public class MainMenuPage : UIPagePresenter<MainMenuView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnBeforeShow()
        {
            View.openPopupButton.onClick.AddListener(() => _ui.Popup<SettingsPopup>());
            View.openOverlayButton.onClick.AddListener(() => _ui.Overlay<HudOverlay>());
        }

        protected override void OnAfterHide()
        {
            View.openPopupButton.onClick.RemoveAllListeners();
            View.openOverlayButton.onClick.RemoveAllListeners();
        }
    }

    [UIPrefab("SettingsPopup")]
    public class SettingsPopup : UIPopupPresenter<SettingsView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnBeforeShow()
        {
            // 서로 다른 타입(ConfirmPopup)을 스택에 쌓아 LIFO 시연
            View.openConfirmButton.onClick.AddListener(() => _ui.Popup<ConfirmPopup>());
            View.closeButton.onClick.AddListener(Hide);
        }

        protected override void OnAfterHide()
        {
            View.openConfirmButton.onClick.RemoveAllListeners();
            View.closeButton.onClick.RemoveAllListeners();
        }
    }

    [UIPrefab("ConfirmPopup")]
    public class ConfirmPopup : UIPopupPresenter<ConfirmView>
    {
        protected override void OnBeforeShow() => View.closeButton.onClick.AddListener(Hide);
        protected override void OnAfterHide() => View.closeButton.onClick.RemoveAllListeners();
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
