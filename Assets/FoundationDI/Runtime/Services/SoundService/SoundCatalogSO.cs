using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public struct SoundEntry
    {
        public string Key;          // 논리 이름 (Play 인자, 드롭다운 표시)
        public string ResourceKey;  // (제거 예정) IResourceService 로드 키
        public AudioClip Clip;      // 직접 참조
        public bool Preload;        // 프리로드 대상 여부
    }

    public interface ISoundCatalog
    {
        bool TryGetResourceKey(string key, out string resourceKey); // (제거 예정)
        bool TryGetClip(string key, out AudioClip clip);
        IReadOnlyList<string> Keys { get; }
        IEnumerable<string> PreloadResourceKeys { get; }            // (제거 예정)
        IEnumerable<AudioClip> PreloadClips { get; }
    }

    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "DarkNaku/SoundCatalog")]
    public sealed class SoundCatalogSO : ScriptableObject, ISoundCatalog
    {
        [SerializeField] private List<SoundEntry> _entries = new();

        private Dictionary<string, string> _map;
        private Dictionary<string, AudioClip> _clipMap;
        private List<string> _keys;

        public IReadOnlyList<string> Keys
        {
            get
            {
                EnsureBuilt();
                return _keys;
            }
        }

        public IEnumerable<string> PreloadResourceKeys
        {
            get
            {
                foreach (var entry in _entries)
                {
                    if (entry.Preload)
                    {
                        yield return entry.ResourceKey;
                    }
                }
            }
        }

        public IEnumerable<AudioClip> PreloadClips
        {
            get
            {
                foreach (var entry in _entries)
                {
                    if (entry.Preload && entry.Clip != null)
                    {
                        yield return entry.Clip;
                    }
                }
            }
        }

        public bool TryGetResourceKey(string key, out string resourceKey)
        {
            EnsureBuilt();
            return _map.TryGetValue(key, out resourceKey);
        }

        public bool TryGetClip(string key, out AudioClip clip)
        {
            EnsureBuilt();
            return _clipMap.TryGetValue(key, out clip);
        }

        private void EnsureBuilt()
        {
            if (_map != null) return;

            _map = new Dictionary<string, string>();
            _clipMap = new Dictionary<string, AudioClip>();
            _keys = new List<string>();

            foreach (var entry in _entries)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;

                if (_map.ContainsKey(entry.Key))
                {
                    Debug.LogWarning($"[SoundCatalogSO] Duplicate key '{entry.Key}', overwriting with last value.");
                }
                else
                {
                    _keys.Add(entry.Key);
                }

                _map[entry.Key] = entry.ResourceKey;
                _clipMap[entry.Key] = entry.Clip;
            }
        }
    }
}
