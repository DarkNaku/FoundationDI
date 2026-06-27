using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Samples
{
    public class ModalHostView : UIView
    {
        [SerializeField] public Button askButton;
        [SerializeField] public Button dummyButton;   // 모달 중 비활성 확인용
        [SerializeField] public Text resultLabel;
    }
}
