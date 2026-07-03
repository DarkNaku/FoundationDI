using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class HapticServiceVContainerExtensions
    {
        /// <summary>
        /// HapticService를 컨테이너에 등록한다. 외부 리소스 의존이 없어 추가 인자는 불필요하다.
        /// </summary>
        public static void RegisterHapticService(this IContainerBuilder builder)
        {
            builder.Register<IHapticService, HapticService>(Lifetime.Singleton);
        }
    }
}
