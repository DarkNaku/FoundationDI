using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

namespace FoundationDI.Host
{
    /// 테스트 허브 메인 페이지. 서비스별 테스트 페이지로 진입하는 버튼을 보유한다.
    public class MainTestView : UIView
    {
        [SerializeField] public Button hapticTestButton;
    }
}
