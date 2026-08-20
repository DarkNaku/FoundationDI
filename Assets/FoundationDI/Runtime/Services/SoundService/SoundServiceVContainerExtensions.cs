using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class SoundServiceVContainerExtensions
    {
        /// <summary>
        /// SoundService를 컨테이너에 등록한다. 볼륨 영속화는 PlayerPrefs 기본 구현을 사용한다.
        /// </summary>
        /// <param name="settings">데이터 컬렉션과 오클루전 설정을 담은 에셋.</param>
        public static void RegisterSoundService(this IContainerBuilder builder, SoundServiceSettings settings)
        {
            builder.RegisterInstance(settings);
            builder.Register<ISoundVolumeStorage, PlayerPrefsVolumeStorage>(Lifetime.Singleton);
            builder.Register<SoundService>(Lifetime.Singleton)
                .As<ISoundService>()
                .As<ISoundEngine>();
        }
    }
}
