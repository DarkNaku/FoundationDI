using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class SoundServiceVContainerExtensions
    {
        /// <summary>
        /// SoundService를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (SoundService가 클립 로드를 IResourceService에 위임함).
        /// </summary>
        public static void RegisterSoundService(this IContainerBuilder builder, SoundCatalog catalog)
        {
            builder.RegisterInstance<ISoundCatalog>(catalog);
            builder.Register<ISoundService, SoundService>(Lifetime.Singleton);
        }
    }
}
