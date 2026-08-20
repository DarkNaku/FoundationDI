using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>SFX 태그 → 클립 묶음 데이터베이스. 에디터의 Audio Creator가 편집한다.</summary>
    [CreateAssetMenu(fileName = "SoundCollection", menuName = "DarkNaku/FoundationDI/Sound Collection")]
    public class SoundDataCollection : ScriptableObject
    {
        [SerializeField] private SoundData[] _sounds = Array.Empty<SoundData>();

        private Dictionary<string, SoundData> _soundsDictionary = new();

        public SoundData[] Sounds => _sounds;

        private void OnEnable()
        {
            Init();
        }

        /// <summary>태그로 사운드 데이터를 찾는다. 없으면 경고 후 null을 반환한다.</summary>
        public SoundData GetSound(string tag)
        {
            if (_soundsDictionary == null || _soundsDictionary.Count != (_sounds?.Length ?? 0))
            {
                Init();
            }

            if (_soundsDictionary.TryGetValue(tag, out var soundData)) return soundData;

            Debug.LogWarning($"[SoundService] '{tag}' 태그의 사운드가 존재하지 않습니다.");
            return null;
        }

        public bool CreateSound(AudioClip[] clips, string tag, CompressionPreset compressionPreset,
            bool forceToMono, out string result)
        {
            if (string.IsNullOrEmpty(tag))
            {
                result = "태그가 필요합니다. 이 사운드를 식별할 태그를 입력하세요.";
                return false;
            }

            if (_sounds.Any(soundData => soundData.Tag == tag))
            {
                result = $"'{tag}' 태그가 이미 존재합니다!";
                return false;
            }

            if (clips.Length <= 0)
            {
                result = "오디오 클립을 최소 1개 이상 추가해야 합니다.";
                return false;
            }

            var newSound = new SoundData(tag, clips, compressionPreset, forceToMono);
            var newSounds = new SoundData[_sounds.Length + 1];
            Array.Copy(_sounds, newSounds, _sounds.Length);
            newSounds[^1] = newSound;
            _sounds = newSounds;

            Init();

            result = $"사운드 '{tag}'가 생성되었습니다.";
            return true;
        }

        public bool EditSound(string tag, string newTag, AudioClip[] clips, out string result)
        {
            if (tag != newTag && _sounds.Any(soundData => soundData.Tag == newTag))
            {
                result = $"'{newTag}' 태그가 이미 존재합니다!";
                return false;
            }

            var soundData = GetSound(tag);

            if (soundData == null)
            {
                result = $"'{tag}' 태그의 사운드를 찾을 수 없습니다.";
                return false;
            }

            soundData.Tag = newTag;
            soundData.Clips = clips;

            Init();

            result = $"사운드 '{newTag}'가 수정되었습니다.";
            return true;
        }

        public void RemoveSound(string tagToRemove)
        {
            _sounds = _sounds.Where(soundData => !soundData.Tag.Equals(tagToRemove)).ToArray();
            Init();
        }

        public void RemoveAll()
        {
            _sounds = Array.Empty<SoundData>();
            Init();
        }

        private void Init()
        {
            _soundsDictionary ??= new Dictionary<string, SoundData>();
            _soundsDictionary.Clear();

            if (_sounds == null) return;

            foreach (var soundData in _sounds)
            {
                if (soundData == null || string.IsNullOrEmpty(soundData.Tag)) continue;

                if (!_soundsDictionary.TryAdd(soundData.Tag, soundData))
                {
                    Debug.LogWarning($"[SoundService] 중복된 사운드 태그 '{soundData.Tag}'는 무시됩니다.");
                }
            }
        }
    }
}
