using Reflex.Attributes;
using UnityEngine;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 앱 시작 시 메인 테스트 페이지를 띄우는 부트스트랩.
    /// Reflex에는 IStartable/엔트리포인트가 없다. 씬의 ContainerScope GameObject에
    /// 이 컴포넌트를 붙인다 - 씬 주입이 Awake보다 먼저 끝나므로 Start에서 _ui는 이미 채워져 있다.
    public class TestHubBootstrap : MonoBehaviour
    {
        [Inject] private IUINavigator _ui;

        private void Start() => _ui.Page<MenuPage>();
    }
}
