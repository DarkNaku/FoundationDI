using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;
using R3;

namespace DarkNaku.FoundationDI
{
    public interface ISoundService : IDisposable
    {
        bool SFXEnabled { get; set; }
        bool BGMEnabled { get; set; }
        bool IsPlayingBGM { get; }
        float VolumeSFX { get; set; }
        float VolumeBGM { get; set; }
        void Play(string clipName);
        void PlayBGM(string clipName);
        void StopBGM();
        UniTask PreloadAsync();
    }
    
    public class SoundService : ISoundService
    {
        public bool SFXEnabled
        {
            get => PlayerPrefs.GetInt(SFX_ENABLED, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(SFX_ENABLED, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public bool BGMEnabled
        {
            get => PlayerPrefs.GetInt(BGM_ENABLED, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(BGM_ENABLED, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public float VolumeSFX 
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(SFX_VOLUME, 1f));
            set 
            {
                PlayerPrefs.SetFloat(SFX_VOLUME, value);
                PlayerPrefs.Save();
            }
        }
        
        public float VolumeBGM 
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(BGM_VOLUME, 1f));
            set 
            {
                _bgmPlayer.volume = value;
                
                PlayerPrefs.SetFloat(BGM_VOLUME, value);
                PlayerPrefs.Save();
            }
        }
        
        public bool IsPlayingBGM => _bgmPlayer != null && _bgmPlayer.isPlaying;
        
        private const string SFX_VOLUME = "SFX_VOLUME";
        private const string BGM_VOLUME = "BGM_VOLUME";
        private const string SFX_ENABLED = "SFX_ENABLED";
        private const string BGM_ENABLED = "BGM_ENABLED";
        
        private readonly IResourceService _resourceService;
        private readonly ISoundCatalog _catalog;
        private readonly Transform _root;
        private readonly Dictionary<string, AudioClip> _table;
        private AudioSource _bgmPlayer;
        private HashSet<AudioSource> _sfxPlayers = new();
        private HashSet<string> _playedClipInThisFrame = new();
        private IDisposable _disposable;

        public SoundService(IResourceService resourceService, ISoundCatalog catalog)
        {
            _resourceService = resourceService;
            _catalog = catalog;
            _table = new Dictionary<string, AudioClip>();

            var root = new GameObject("[SoundService]");

            _root = root.transform;

            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(root);
            }

            _bgmPlayer = new GameObject("BGM Player").AddComponent<AudioSource>();
            _bgmPlayer.transform.parent = _root;

            _disposable = Observable.EveryUpdate(UnityFrameProvider.PostLateUpdate).Subscribe(OnPostLateUpdate);
        }
        
        public void Dispose()
        {
            _disposable.Dispose();

            foreach (var key in _table.Keys)
            {
                _resourceService.Release(key);
            }

            _table.Clear();

            if (Application.isPlaying)
            {
                Object.Destroy(_root.gameObject);
            }
            else
            {
                Object.DestroyImmediate(_root.gameObject);
            }
        }
        
        public void Play(string key)
        {
            if (Mathf.Approximately(VolumeSFX, 0f) || !SFXEnabled) return;
            // 프레임 중복 차단은 호출 측 논리 키 기준(_table 캐시는 리소스 키 기준).
            if (_playedClipInThisFrame.Contains(key)) return;

            if (!_catalog.TryGetResourceKey(key, out var resourceKey))
            {
                Debug.LogError($"[SoundService] Play : Key not found in catalog. ({key})");
                return;
            }

            var player = GetPlayer();
            var clip = GetClip(resourceKey);

            if (clip == null) return;

            player.clip = clip;
            player.loop = false;
            player.volume = VolumeSFX;
            player.Play();

            _playedClipInThisFrame.Add(key);
        }

        public void PlayBGM(string key)
        {
            if (Mathf.Approximately(VolumeBGM, 0f) || !BGMEnabled) return;

            if (!_catalog.TryGetResourceKey(key, out var resourceKey))
            {
                Debug.LogError($"[SoundService] PlayBGM : Key not found in catalog. ({key})");
                return;
            }

            var clip = GetClip(resourceKey);

            if (clip == null) return;

            if (_bgmPlayer.isPlaying)
            {
                _bgmPlayer.Stop();
            }

            _bgmPlayer.clip = clip;
            _bgmPlayer.loop = true;
            _bgmPlayer.volume = VolumeBGM;
            _bgmPlayer.Play();
        }

        public void StopBGM()
        {
            _bgmPlayer.Stop();
        }

        public async UniTask PreloadAsync()
        {
            var resourceKeys = _catalog.PreloadResourceKeys;
            if (resourceKeys == null) return;

            var tasks = new List<UniTask>();

            foreach (var resourceKey in resourceKeys.Distinct())
            {
                tasks.Add(PreloadOneAsync(resourceKey));
            }

            await UniTask.WhenAll(tasks);
        }

        private async UniTask PreloadOneAsync(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return;
            if (_table.ContainsKey(resourceKey)) return;

            var clip = await _resourceService.LoadAsync<AudioClip>(resourceKey);

            // await 동안 다른 경로(Play의 동기 Load 또는 동시 Preload)가 이미 캐시했으면
            // 이번 LoadAsync로 증가한 잉여 참조를 해제한다(refcount 누수 방지).
            if (_table.ContainsKey(resourceKey))
            {
                _resourceService.Release(resourceKey);
                return;
            }

            if (clip != null)
            {
                _table[resourceKey] = clip;
            }
        }

        private void OnPostLateUpdate(Unit unit) 
        {
            _playedClipInThisFrame.Clear();
        }
        
        private AudioSource GetPlayer() 
        {
            AudioSource player = null;

            foreach (var source in _sfxPlayers) 
            {
                if (source.isPlaying) continue;
                player = source;
                break;
            }

            if (player == null) 
            {
                player = new GameObject("SFX Player").AddComponent<AudioSource>();
                player.transform.parent = _root;

                if (Application.isPlaying)
                {
                    Object.DontDestroyOnLoad(player.gameObject);
                }

                _sfxPlayers.Add(player);
            }

            return player;
        }

        private AudioClip GetClip(string key)
        {
            if (_table.TryGetValue(key, out var clip))
            {
                return clip;
            }

            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[SoundService] GetClip : Key is wrong.");
                return null;
            }

            clip = _resourceService.Load<AudioClip>(key);

            if (clip == null)
            {
                Debug.LogError($"[SoundService] GetClip : Clip is null. ({key})");
                return null;
            }

            _table.Add(key, clip);

            return clip;
        }
    }
}