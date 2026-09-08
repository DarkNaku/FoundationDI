using UnityEngine;
using Reflex.Attributes;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 테스트 허브 메인 페이지. 서비스별 테스트 페이지로 진입한다.
    [UIPrefab("MainTestPage")]
    public class MainTestPage : UIPagePresenter<MainTestView>
    {
        [Inject] private IUINavigator _ui;

        protected override void OnInitialize()
        {
            View.hapticTestButton.onClick.AddListener(() => _ui.Page<HapticTestPage>());
        }
    }

    /// HapticService의 모든 동작을 버튼으로 호출/검증하는 페이지.
    [UIPrefab("HapticTestPage")]
    public class HapticTestPage : UIPagePresenter<HapticTestView>
    {
        [Inject] private IUINavigator _ui;
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
            {
                View.enabledLabel.text = _haptic.Enabled ? "Haptic: ON" : "Haptic: OFF";
            }
        }

        private void SetStatus(string message)
        {
            if (View.statusLabel != null)
            {
                View.statusLabel.text = message;
            }
        }
    }

    /// 앱 시작 시 메인 테스트 페이지를 띄우는 부트스트랩.
    /// Reflex에는 IStartable/엔트리포인트가 없다. 씬의 ContainerScope GameObject에
    /// 이 컴포넌트를 붙인다 - 씬 주입이 Awake보다 먼저 끝나므로 Start에서 _ui는 이미 채워져 있다.
    public class TestHubBootstrap : MonoBehaviour
    {
        [Inject] private IUINavigator _ui;

        private void Start() => _ui.Page<MenuPage>();
    }
}
