using Reflex.Core;
using UnityEngine;

namespace DarkNaku.FoundationDI.Samples
{
    /// <summary>
    /// 사운드 샘플 컴포지션 루트.
    /// SoundService만 등록한다(UINavigator는 쓰지 않는다).
    /// 씬 배치 컴포넌트의 주입은 ContainerScope가 직접 한다 - 별도 등록이 필요 없다.
    /// </summary>
    public class SoundSampleInstaller : MonoBehaviour, IInstaller
    {
        [Tooltip("Tools > FoundationDI > Sound > Settings 에서 만든 설정 에셋.")]
        [SerializeField] private SoundServiceSettings _soundSettings;

        public void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterSoundService(_soundSettings);
        }
    }
}
