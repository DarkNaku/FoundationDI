using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class UIManagerVContainerExtensions
    {
        /// <summary>
        /// UIManager를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (UIInstanceFactory가 프리팹 로드를 IResourceService에 위임함).
        /// </summary>
        public static void RegisterUIManager(this IContainerBuilder builder, UIManagerSettings settings)
        {
            builder.RegisterInstance(settings);
            builder.Register<UIInstanceFactory>(Lifetime.Singleton);
            builder.Register<UIManager>(Lifetime.Singleton).As<IUIManager>();
        }
    }
}
