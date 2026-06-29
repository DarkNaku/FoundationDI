using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI
{
    public static class InjectorVContainerExtensions
    {
        /// <summary>
        /// 씬 컴포넌트 주입 인프라(InjectorService)를 EntryPoint로 등록한다.
        /// 호스트 LifetimeScope의 Configure에서 호출한다.
        /// </summary>
        public static void RegisterInjector(this IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<InjectorService>();
        }
    }
}
