using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 사운드 재생 진입점.
    /// <see cref="Sound"/>/<see cref="Music"/>/<see cref="Playlist"/>/<see cref="DynamicMusic"/>는
    /// 반드시 이 서비스의 팩토리 메서드로 생성해야 한다.
    /// </summary>
    public interface ISoundService : IDisposable
    {
        /// <summary>주입된 설정 에셋.</summary>
        SoundServiceSettings Settings { get; }

        /// <summary>SFX 태그로 새 <see cref="Sound"/>를 만든다.</summary>
        Sound CreateSound(SFX sfx);

        /// <summary>SFX 태그 문자열로 새 <see cref="Sound"/>를 만든다.</summary>
        Sound CreateSound(string tag);

        /// <summary>음악 태그로 새 <see cref="Music"/>를 만든다.</summary>
        Music CreateMusic(Track track);

        /// <summary>음악 태그 문자열로 새 <see cref="Music"/>를 만든다.</summary>
        Music CreateMusic(string tag);

        /// <summary>재생 순서대로 나열한 트랙으로 새 <see cref="Playlist"/>를 만든다.</summary>
        Playlist CreatePlaylist(params Track[] tracks);

        /// <summary>재생 순서대로 나열한 태그로 새 <see cref="Playlist"/>를 만든다.</summary>
        Playlist CreatePlaylist(params string[] tags);

        /// <summary>동시에 재생할 트랙들로 새 <see cref="DynamicMusic"/>를 만든다.</summary>
        DynamicMusic CreateDynamicMusic(params Track[] tracks);

        /// <summary>동시에 재생할 태그들로 새 <see cref="DynamicMusic"/>를 만든다.</summary>
        DynamicMusic CreateDynamicMusic(params string[] tags);

        /// <summary>Output 볼륨을 바꾸고 저장한다.</summary>
        /// <param name="output">대상 Output</param>
        /// <param name="value">볼륨: 최소 0, 최대 1</param>
        void ChangeOutputVolume(Output output, float value);

        /// <summary>Output 볼륨을 바꾸고 저장한다.</summary>
        /// <param name="outputName">대상 Output 이름</param>
        /// <param name="value">볼륨: 최소 0, 최대 1</param>
        void ChangeOutputVolume(string outputName, float value);

        /// <summary>마지막으로 저장된 Output 볼륨을 반환한다(없으면 1로 초기화).</summary>
        float GetSavedOutputVolume(string outputName);

        /// <summary>모든 사운드/음악/다이내믹 뮤직/플레이리스트를 일시정지한다.</summary>
        /// <param name="fadeOutTime">페이드 아웃 시간(초)</param>
        void PauseAll(float fadeOutTime = 0f);

        /// <summary>참조 없이 id로 특정 재생을 일시정지한다.</summary>
        void Pause(string id, float fadeOutTime = 0f);

        /// <summary>모든 사운드/음악/다이내믹 뮤직/플레이리스트를 정지한다.</summary>
        void StopAll(float fadeOutTime = 0f);

        /// <summary>참조 없이 id로 특정 재생을 정지한다.</summary>
        void Stop(string id, float fadeOutTime = 0f);

        /// <summary>일시정지된 모든 재생을 재개한다.</summary>
        void ResumeAll(float fadeInTime = 0f);

        /// <summary>참조 없이 id로 특정 재생을 재개한다.</summary>
        void Resume(string id, float fadeInTime = 0f);
    }
}
