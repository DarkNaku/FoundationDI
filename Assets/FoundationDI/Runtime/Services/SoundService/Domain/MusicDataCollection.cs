using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>음악 태그 → 클립 묶음 데이터베이스. 에디터의 Audio Creator가 편집한다.</summary>
    [CreateAssetMenu(fileName = "MusicCollection", menuName = "DarkNaku/FoundationDI/Music Collection")]
    public class MusicDataCollection : ScriptableObject
    {
        [SerializeField] private SoundData[] _musicTracks = Array.Empty<SoundData>();

        private Dictionary<string, SoundData> _musicTracksDictionary = new();

        public SoundData[] MusicTracks => _musicTracks;

        private void OnEnable()
        {
            Init();
        }

        /// <summary>태그로 음악 데이터를 찾는다. 없으면 경고 후 null을 반환한다.</summary>
        public SoundData GetMusicTrack(string tag)
        {
            if (_musicTracksDictionary == null || _musicTracksDictionary.Count != (_musicTracks?.Length ?? 0))
            {
                Init();
            }

            if (_musicTracksDictionary.TryGetValue(tag, out var soundData)) return soundData;

            Debug.LogWarning($"[SoundService] '{tag}' 태그의 음악이 존재하지 않습니다.");
            return null;
        }

        public bool CreateMusicTrack(AudioClip[] clips, string tag, CompressionPreset compressionPreset,
            bool forceToMono, out string result)
        {
            if (string.IsNullOrEmpty(tag))
            {
                result = "태그가 필요합니다. 이 음악을 식별할 태그를 입력하세요.";
                return false;
            }

            if (_musicTracks.Any(soundData => soundData.Tag == tag))
            {
                result = $"'{tag}' 태그가 이미 존재합니다!";
                return false;
            }

            if (clips.Length <= 0)
            {
                result = "오디오 클립을 최소 1개 이상 추가해야 합니다.";
                return false;
            }

            var newTrack = new SoundData(tag, clips, compressionPreset, forceToMono);
            var newTracks = new SoundData[_musicTracks.Length + 1];
            Array.Copy(_musicTracks, newTracks, _musicTracks.Length);
            newTracks[^1] = newTrack;
            _musicTracks = newTracks;

            Init();

            result = $"음악 '{tag}'가 생성되었습니다.";
            return true;
        }

        public bool EditMusic(string tag, string newTag, AudioClip[] clips, out string result)
        {
            if (tag != newTag && _musicTracks.Any(soundData => soundData.Tag == newTag))
            {
                result = $"'{newTag}' 태그가 이미 존재합니다!";
                return false;
            }

            var soundData = GetMusicTrack(tag);

            if (soundData == null)
            {
                result = $"'{tag}' 태그의 음악을 찾을 수 없습니다.";
                return false;
            }

            soundData.Tag = newTag;
            soundData.Clips = clips;

            Init();

            result = $"음악 '{newTag}'가 수정되었습니다.";
            return true;
        }

        public void RemoveMusicTrack(string tagToRemove)
        {
            _musicTracks = _musicTracks.Where(soundData => !soundData.Tag.Equals(tagToRemove)).ToArray();
            Init();
        }

        public void RemoveAll()
        {
            _musicTracks = Array.Empty<SoundData>();
            Init();
        }

        private void Init()
        {
            _musicTracksDictionary ??= new Dictionary<string, SoundData>();
            _musicTracksDictionary.Clear();

            if (_musicTracks == null) return;

            foreach (var soundData in _musicTracks)
            {
                if (soundData == null || string.IsNullOrEmpty(soundData.Tag)) continue;

                if (!_musicTracksDictionary.TryAdd(soundData.Tag, soundData))
                {
                    Debug.LogWarning($"[SoundService] 중복된 음악 태그 '{soundData.Tag}'는 무시됩니다.");
                }
            }
        }
    }
}
