using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 빌더(Sound/Music/Playlist/DynamicMusic)와 <see cref="SoundSource"/>가 사용하는 내부 seam.
    /// 공개 API인 <see cref="ISoundService"/>와 분리해 외부에 노출하지 않는다.
    /// </summary>
    internal interface ISoundEngine
    {
        SoundServiceSettings Settings { get; }

        /// <summary>사용 중이 아닌 재생 유닛을 풀에서 꺼낸다. 없으면 새로 만든다.</summary>
        SoundSource GetSource();

        /// <summary>
        /// 이번 프레임에 이 클립을 재생해도 되는지 묻고, 되면 슬롯을 예약한다.
        /// 같은 프레임에 같은 클립으로 두 번째부터는 false를 돌려준다.
        /// 판정은 클립 단위로 전역이다 — 서로 다른 빌더가 같은 클립을 내는 경우도 막아야 한다.
        /// </summary>
        /// <param name="clip">재생할 클립. null이면 항상 true(태그 조회 실패 경로를 가리지 않는다).</param>
        bool TryReserveClipThisFrame(AudioClip clip);

        AudioMixerGroup GetOutput(Output output);

        AudioMixerGroup GetOutput(string outputName);

        /// <summary>SFX 태그의 클립을 가져온다. index가 -1이면 무작위.</summary>
        AudioClip GetSFX(string tag, int index = -1);

        /// <summary>음악 태그의 클립을 가져온다. index가 -1이면 무작위.</summary>
        AudioClip GetTrack(string tag, int index = -1);

        bool TryGetListener(out AudioListener listener);

        /// <summary>리스너와 소스 사이의 차폐 정도를 0(안 가림)~1(완전히 가림)로 계산한다.</summary>
        void CalculateOcclusion(Vector3 listenerPosition, Vector3 sourcePosition, out float factor);
    }
}
