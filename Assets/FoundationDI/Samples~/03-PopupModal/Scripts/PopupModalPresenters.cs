using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    [UIPrefab("ConfirmDialog")]
    public class ConfirmDialog : UIPopupPresenter<ConfirmDialogView>
    {
        public bool Confirmed { get; private set; }

        private UnityEngine.Events.UnityAction _onYes;
        private UnityEngine.Events.UnityAction _onNo;

        // View는 풀에서 재사용되므로 버튼 구독은 OnBeforeShow에서 걸고
        // OnAfterHide에서 해제한다. 해제하지 않으면 다음 Show 시 중복 핸들러가 쌓인다.
        protected override void OnBeforeShow()
        {
            _onYes = () => { Confirmed = true; Hide(); };
            _onNo  = () => { Confirmed = false; Hide(); };
            View.yesButton.onClick.AddListener(_onYes);
            View.noButton.onClick.AddListener(_onNo);
        }

        protected override void OnAfterHide()
        {
            View.yesButton.onClick.RemoveListener(_onYes);
            View.noButton.onClick.RemoveListener(_onNo);
            _onYes = null;
            _onNo  = null;
        }
    }

    [UIPrefab("ModalHost")]
    public class ModalHostPage : UIPagePresenter<ModalHostView>
    {
        [Inject] private IUINavigator _ui;

        protected override void OnInitialize()
        {
            View.dummyButton.onClick.AddListener(() => Debug.Log("[Modal] dummy clicked"));
            View.askButton.onClick.AddListener(() =>
            {
                // 결과 반환: 콜백 파라미터가 ConfirmDialog 그대로라 캡처 없이 바로 읽는다.
                _ui.Popup<ConfirmDialog>()
                   .OnAfterHide(p => View.resultLabel.text = p.Confirmed ? "결과: 확인" : "결과: 취소");
            });
        }
    }

    public class PopupModalDemo : IStartable
    {
        private readonly IUINavigator _ui;
        public PopupModalDemo(IUINavigator ui) => _ui = ui;
        public void Start() => _ui.Page<ModalHostPage>();
    }
}
