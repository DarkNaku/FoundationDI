using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    /// 샘플 공통 부트스트랩. 각 샘플은 이를 상속해 Configure에서 RegisterEntryPoint로 Demo 드라이버를 추가한다.
    /// 씬에 배치된 스코프이므로 여기 등록된 UINavigator는 씬 수명을 갖는다.
    public class SampleLifetimeScope : LifetimeScope
    {
        public UINavigatorSettings settings;
        public SampleResourceService resourceService;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance<IResourceService>(resourceService);
            builder.RegisterUINavigator(settings);
        }
    }
}
