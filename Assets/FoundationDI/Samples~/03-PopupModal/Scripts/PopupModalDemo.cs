using Reflex.Attributes;
using UnityEngine;

namespace DarkNaku.FoundationDI.Samples
{
    /// Reflex에는 엔트리포인트가 없다. 씬의 ContainerScope GameObject에 붙인다.
    public class PopupModalDemo : MonoBehaviour
    {
        [Inject] private IUINavigator _ui;

        private void Start() => _ui.Page<ModalHostPage>();
    }
}
