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
        /// <remarks>
        /// 반드시 루트 LifetimeScope에서 한 번만 호출한다. InjectorService는 정적 컨테이너
        /// 참조를 공유하므로(단일 컨테이너 모델), 자식 스코프에서 중복 등록하면 루트의
        /// 주입 대상이 깨질 수 있다.
        /// </remarks>
        public static void RegisterInjector(this IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<InjectorService>();
        }
    }
}
