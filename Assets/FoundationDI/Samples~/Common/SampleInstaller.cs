using Reflex.Core;
using UnityEngine;

namespace DarkNaku.FoundationDI.Samples
{
    /// 샘플 공통 부트스트랩. 각 샘플은 이를 상속해 InstallBindings에서 추가 등록을 한다.
    /// 씬의 ContainerScope와 같은 GameObject에 붙이므로 여기 등록된 UINavigator는 씬 수명을 갖는다.
    /// Demo 드라이버는 Reflex에 엔트리포인트가 없어 MonoBehaviour로 씬에 배치한다.
    public class SampleInstaller : MonoBehaviour, IInstaller
    {
        public UINavigatorSettings settings;
        public SampleResourceService resourceService;

        public virtual void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterValue(resourceService, new[] { typeof(IResourceService) });
            builder.RegisterUINavigator(settings);
        }
    }
}
