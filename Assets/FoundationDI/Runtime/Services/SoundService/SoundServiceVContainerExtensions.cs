using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public static class SoundServiceVContainerExtensions
    {
        /// <summary>
        /// SoundService를 컨테이너에 등록한다. 볼륨 영속화는 PlayerPrefs 기본 구현을 사용한다.
        /// </summary>
        /// <param name="settings">데이터 컬렉션과 오클루전 설정을 담은 에셋.</param>
        public static ContainerBuilder RegisterSoundService(this ContainerBuilder builder,
                                                            SoundServiceSettings settings)
        {
            builder.RegisterValue(settings);
            builder.RegisterType(
                typeof(PlayerPrefsVolumeStorage), new[] { typeof(ISoundVolumeStorage) },
                Lifetime.Singleton, Resolution.Lazy);

            // 계약 둘이 한 바인딩을 공유한다 - 싱글턴이므로 같은 인스턴스가 나온다.
            return builder.RegisterType(
                typeof(SoundService), new[] { typeof(ISoundService), typeof(ISoundEngine) },
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
