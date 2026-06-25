using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class UIManagerVContainerExtensions
    {
        public static void RegisterUIManager(this IContainerBuilder builder, UIManagerSettings settings)
        {
            builder.RegisterInstance(settings);
            builder.Register<IUIAssetLoader, ResourcesUILoader>(Lifetime.Singleton);
            builder.Register<UIInstanceFactory>(Lifetime.Singleton);
            builder.Register<UIManager>(Lifetime.Singleton).As<IUIManager>();
        }
    }
}
