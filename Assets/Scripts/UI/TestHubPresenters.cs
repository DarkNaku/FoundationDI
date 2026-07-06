using UnityEngine;
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 테스트 허브 메인 페이지. 서비스별 테스트 페이지로 진입한다.
    [UIPrefab("MainTestPage")]
    public class MainTestPage : UIPagePresenter<MainTestView>
    {
        [Inject] private IUIManager _ui;

        protected override void OnInitialize()
        {
            View.hapticTestButton.onClick.AddListener(() => _ui.Page<HapticTestPage>());
        }
    }

    /// HapticService의 모든 동작을 버튼으로 호출/검증하는 페이지.
    [UIPrefab("HapticTestPage")]
    public class HapticTestPage : UIPagePresenter<HapticTestView>
    {
        [Inject] private IUIManager _ui;
        [Inject] private IHapticService _haptic;

        protected override void OnInitialize()
        {
            View.lightButton.onClick.AddListener(() => Impact(HapticImpact.Light));
            View.mediumButton.onClick.AddListener(() => Impact(HapticImpact.Medium));
            View.heavyButton.onClick.AddListener(() => Impact(HapticImpact.Heavy));
            View.softButton.onClick.AddListener(() => Impact(HapticImpact.Soft));
            View.rigidButton.onClick.AddListener(() => Impact(HapticImpact.Rigid));

            View.successButton.onClick.AddListener(() => Notify(HapticNotification.Success));
            View.warningButton.onClick.AddListener(() => Notify(HapticNotification.Warning));
            View.errorButton.onClick.AddListener(() => Notify(HapticNotification.Error));

            View.selectionButton.onClick.AddListener(() =>
            {
                _haptic.Selection();
                SetStatus("Selection() 호출");
            });

            View.enabledButton.onClick.AddListener(ToggleEnabled);
            View.backButton.onClick.AddListener(() => _ui.Page<MainTestPage>());
        }

        protected override void OnBeforeShow()
        {
            RefreshEnabledLabel();
            SetStatus("버튼을 눌러 햅틱을 테스트하세요. (에디터는 진동 없음)");
        }

        private void Impact(HapticImpact style)
        {
            _haptic.Impact(style);
            SetStatus($"Impact({style}) 호출");
        }

        private void Notify(HapticNotification type)
        {
            _haptic.Notification(type);
            SetStatus($"Notification({type}) 호출");
        }

        private void ToggleEnabled()
        {
            _haptic.Enabled = !_haptic.Enabled;
            RefreshEnabledLabel();
            SetStatus($"Enabled = {_haptic.Enabled}");
        }

        private void RefreshEnabledLabel()
        {
            if (View.enabledLabel != null)
                View.enabledLabel.text = _haptic.Enabled ? "Haptic: ON" : "Haptic: OFF";
        }

        private void SetStatus(string message)
        {
            if (View.statusLabel != null)
                View.statusLabel.text = message;
        }
    }

    /// 앱 시작 시 메인 테스트 페이지를 띄우는 부트스트랩.
    public class TestHubBootstrap : IStartable
    {
        private readonly IUIManager _ui;

        public TestHubBootstrap(IUIManager ui) => _ui = ui;

        public void Start() => _ui.Page<MainTestPage>();
    }
}
