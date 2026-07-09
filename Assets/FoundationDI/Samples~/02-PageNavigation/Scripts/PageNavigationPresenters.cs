using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public struct CharacterDetailParams { public int CharacterId; }

    // View는 풀 재사용, Presenter는 매 Show마다 새로 생성 → 버튼 구독은
    // OnBeforeShow에서 걸고 OnAfterHide에서 해제한다(재탐색 시 중복 방지).
    [UIPrefab("Title")]
    public class TitlePage : UIPagePresenter<TitleView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnBeforeShow()
            => View.nextButton.onClick.AddListener(() => _ui.Page<CharacterListPage>());

        protected override void OnAfterHide()
            => View.nextButton.onClick.RemoveAllListeners();
    }

    [UIPrefab("CharacterList")]
    public class CharacterListPage : UIPagePresenter<CharacterListView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnBeforeShow()
        {
            for (int i = 0; i < View.characterButtons.Length; i++)
            {
                int id = i + 1;
                View.characterButtons[i].onClick.AddListener(() =>
                    _ui.Page<CharacterDetailPage>()
                       .With(new CharacterDetailParams { CharacterId = id })
                       .OnBeforeShow(p => Debug.Log("[Lifecycle] OnBeforeShow"))
                       .OnAfterShow(p => Debug.Log("[Lifecycle] OnAfterShow"))
                       .OnBeforeHide(p => Debug.Log("[Lifecycle] OnBeforeHide"))
                       .OnAfterHide(p => Debug.Log("[Lifecycle] OnAfterHide")));
            }
        }

        protected override void OnAfterHide()
        {
            for (int i = 0; i < View.characterButtons.Length; i++)
                View.characterButtons[i].onClick.RemoveAllListeners();
        }
    }

    [UIPrefab("CharacterDetail")]
    public class CharacterDetailPage : UIPagePresenter<CharacterDetailView>, IConfigurable<CharacterDetailParams>
    {
        [Inject] private IUIManager _ui;
        private CharacterDetailParams _params;

        // Configure는 View 바인딩 전에 실행되므로 params만 저장하고, View 접근은 OnBeforeShow에서.
        public void Configure(CharacterDetailParams p) => _params = p;

        protected override void OnBeforeShow()
        {
            View.idLabel.text = $"Character #{_params.CharacterId}";
            View.backButton.onClick.AddListener(() => _ui.Page<CharacterListPage>());
        }

        protected override void OnAfterHide()
            => View.backButton.onClick.RemoveAllListeners();
    }

    public class PageNavigationDemo : IStartable
    {
        private readonly IUIManager _ui;
        public PageNavigationDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<TitlePage>();
    }
}
