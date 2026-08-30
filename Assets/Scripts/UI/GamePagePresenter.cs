using VContainer;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 게임 페이지. 메뉴로 복귀도 페이지 전환(Fade → 검은 페이드)으로 동작한다.
    [UIPrefab("GamePage")]
    public class GamePage : UIPagePresenter<GamePageView>
    {
        [Inject] private IUINavigator _ui;

        protected override void OnInitialize()
        {
            View.backButton.onClick.RemoveAllListeners();
            View.backButton.onClick.AddListener(() => _ui.Page<MenuPage>());
        }
    }
}
