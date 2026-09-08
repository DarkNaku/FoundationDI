using Reflex.Attributes;
using UnityEngine;

namespace DarkNaku.FoundationDI.Samples
{
    /// Reflex에는 엔트리포인트가 없다. 씬의 ContainerScope GameObject에 붙인다.
    public class OverlayDemo : MonoBehaviour
    {
        [Inject] private IUINavigator _ui;

        private void Start()
        {
            _ui.Overlay<BackgroundBelowOverlay>();   // Below 배경
            _ui.Page<OverlayHostPage>();             // Page — OnBeforeShow에서 Above HUD 생성
        }
}
