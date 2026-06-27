using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public struct CharacterDetailParams { public int CharacterId; }

    [UIPrefab("Title")]
    public class TitlePage : UIPagePresenter<TitleView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
            => View.nextButton.onClick.AddListener(() => _ui.Page<CharacterListPage>());
    }

    [UIPrefab("CharacterList")]
    public class CharacterListPage : UIPagePresenter<CharacterListView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
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
    }

    [UIPrefab("CharacterDetail")]
    public class CharacterDetailPage : UIPagePresenter<CharacterDetailView>, IConfigurable<CharacterDetailParams>
    {
        [Inject] private IUIManager _ui;
        private CharacterDetailParams _params;

        public void Configure(CharacterDetailParams p) => _params = p;

        protected override void OnBeforeShow() => View.idLabel.text = $"Character #{_params.CharacterId}";

        protected override void OnInitialize()
            => View.backButton.onClick.AddListener(() => _ui.Page<CharacterListPage>());
    }

    public class PageNavigationDemo : IStartable
    {
        private readonly IUIManager _ui;
        public PageNavigationDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<TitlePage>();
    }
}
