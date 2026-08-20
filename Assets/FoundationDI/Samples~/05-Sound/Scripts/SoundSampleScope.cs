using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    /// <summary>
    /// 사운드 샘플 컴포지션 루트.
    /// SoundService와 씬 컴포넌트 주입 인프라만 등록한다(UIService는 쓰지 않는다).
    /// </summary>
    public class SoundSampleScope : LifetimeScope
    {
        [Tooltip("Tools > FoundationDI > Sound > Settings 에서 만든 설정 에셋.")]
        [SerializeField] private SoundServiceSettings _soundSettings;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterSoundService(_soundSettings);

            // SoundSampleDemo, SoundButton, MusicZone 같은 씬 배치 컴포넌트에 주입하려면 필요하다.
            builder.RegisterInjector();
        }
    }
}
