using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    [UIPrefab("ConfirmDialog")]
    public class ConfirmDialog : UIPopupPresenter<ConfirmDialogView>
    {
        public bool Confirmed { get; private set; }

        protected override void OnInitialize()
        {
            View.yesButton.onClick.AddListener(() => { Confirmed = true; Hide(); });
            View.noButton.onClick.AddListener(() => { Confirmed = false; Hide(); });
        }
    }

    [UIPrefab("ModalHost")]
    public class ModalHostPage : UIPagePresenter<ModalHostView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
        {
            View.dummyButton.onClick.AddListener(() => Debug.Log("[Modal] dummy clicked"));
            View.askButton.onClick.AddListener(() =>
            {
                // 결과 반환: Presenter의 Result 프로퍼티를 OnAfterHide 콜백에서 읽는다.
                var dialog = _ui.Popup<ConfirmDialog>();
                dialog.OnAfterHide(_ => View.resultLabel.text = dialog.Confirmed ? "결과: 확인" : "결과: 취소");
            });
        }
    }

    public class PopupModalDemo : IStartable
    {
        private readonly IUIManager _ui;
        public PopupModalDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<ModalHostPage>();
    }
}
