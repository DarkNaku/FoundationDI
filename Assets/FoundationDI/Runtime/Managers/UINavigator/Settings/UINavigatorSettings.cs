using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UINavigatorSettings", menuName = "DarkNaku/UINavigatorSettings")]
    public sealed class UINavigatorSettings : ScriptableObject
    {
        // UINavigator가 런타임에 인스턴스화할 캔버스 루트 프리팹.
        // 캔버스 렌더 모드/CanvasScaler/레이어 구성은 전부 이 프리팹이 결정한다.
        // 비워두면 UIRoot.CreateDefault()로 폴백한다.
        [SerializeField] private UIRoot _rootPrefab;

        // setter는 테스트 전용이다(InternalsVisibleTo). 런타임에는 인스펙터가 유일한 설정 경로다.
        public UIRoot RootPrefab
        {
            get => _rootPrefab;
            internal set => _rootPrefab = value;
        }
    }
}
