using VContainer;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 메뉴 페이지. 탭(상점/메인/랭킹)은 내부 pager의 coordinated 슬라이드로 전환하고,
    /// 게임 화면으로는 UIService 페이지 전환(Fade → 검은 페이드)으로 이동한다.
    [UIPrefab("MenuPage")]
    public class MenuPage : UIPagePresenter<MenuPageView>
    {
        [Inject] private IUIService _ui;

        protected override void OnInitialize()
        {
            // 탭 버튼 → pager 인덱스 이동(슬라이드 방향은 pager가 인덱스 비교로 자동 결정).
            View.shopTab.onClick.RemoveAllListeners();
            View.shopTab.onClick.AddListener(() => View.pager.GoTo(0));
            View.mainTab.onClick.RemoveAllListeners();
            View.mainTab.onClick.AddListener(() => View.pager.GoTo(1));
            View.rankingTab.onClick.RemoveAllListeners();
            View.rankingTab.onClick.AddListener(() => View.pager.GoTo(2));

            // 게임 화면으로 이동은 페이지 전환(Fade). 메뉴가 검게 빠졌다가 게임이 검은 화면에서 등장.
            View.startGameButton.onClick.RemoveAllListeners();
            View.startGameButton.onClick.AddListener(() => _ui.Page<GamePage>());
        }

        protected override void OnBeforeShow()
        {
            View.pager.SetImmediate(1); // 시작 탭 = 메인(index 1)
            View.pager.Relayout();
        }
    }
}
