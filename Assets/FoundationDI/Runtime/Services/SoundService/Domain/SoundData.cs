using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 하나의 태그에 묶인 오디오 클립 묶음. 같은 태그에 여러 클립을 등록하면
    /// 재생 시 무작위로 하나가 선택되어 반복감을 줄인다.
    /// </summary>
    [Serializable]
    public class SoundData
    {
        [SerializeField] private string _tag;
        [SerializeField] private AudioClip[] _clips;
        [SerializeField] private CompressionPreset _compressionPreset;
        [SerializeField] private bool _forceToMono;

        public string Tag { get => _tag; set => _tag = value; }
        public AudioClip[] Clips { get => _clips; set => _clips = value; }
        public CompressionPreset CompressionPreset { get => _compressionPreset; set => _compressionPreset = value; }
        public bool ForceToMono { get => _forceToMono; set => _forceToMono = value; }

        public SoundData(string tag, AudioClip[] clips, CompressionPreset compressionPreset, bool forceToMono)
        {
            _tag = tag;
            _clips = clips;
            _compressionPreset = compressionPreset;
            _forceToMono = forceToMono;
        }

        /// <summary>
        /// 인덱스로 클립을 가져온다. -1이면 무작위, 범위를 벗어나면 경고 후 무작위로 대체한다.
        /// </summary>
        public AudioClip GetClip(int index)
        {
            if (_clips == null || _clips.Length == 0)
            {
                Debug.LogWarning($"[SoundService] '{_tag}'에 등록된 오디오 클립이 없습니다.");
                return null;
            }

            if (index > _clips.Length - 1)
            {
                Debug.LogWarning($"[SoundService] 인덱스 '{index}'에 오디오 클립이 없습니다. " +
                                 $"'{_tag}'에는 {_clips.Length}개의 클립이 등록되어 있습니다.");
                index = -1;
            }

            if (index < 0)
            {
                return _clips[Random.Range(0, _clips.Length)];
            }

            return _clips[index];
        }
    }
}
