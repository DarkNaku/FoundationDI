using UnityEngine.UI;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 메뉴 화면 View. 하단 탭 버튼(상점/메인/랭킹)으로 내부 pager를 전환하고,
    /// "게임 시작" 버튼으로 게임 화면(별도 페이지)으로 이동한다.
    public class MenuPageView : UIView
    {
        public Button shopTab;
        public Button mainTab;
        public Button rankingTab;
        public Button startGameButton;
        public TabPager pager;
    }
}
