using System;
using System.Collections.Generic;
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
        private readonly Transform _root;
        private readonly Dictionary<string, AudioClip> _table;
        private AudioSource _bgmPlayer;
        private HashSet<AudioSource> _sfxPlayers = new();
        private HashSet<string> _playedClipInThisFrame = new();
        private IDisposable _disposable;

        public SoundService(IResourceService resourceService)
        {
            _resourceService = resourceService;
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
        
        public void Play(string clipName)
        {
            if (Mathf.Approximately(VolumeSFX, 0f) || !SFXEnabled) return;
            if (_playedClipInThisFrame.Contains(clipName)) return;

            var player = GetPlayer();
            var clip = GetClip(clipName);

            if (clip == null) return;

            player.clip = clip;
            player.loop = false;
            player.volume = VolumeSFX;
            player.Play();

            _playedClipInThisFrame.Add(clipName);
        }

        public void PlayBGM(string clipName)
        {
            if (Mathf.Approximately(VolumeBGM, 0f) || !BGMEnabled) return;

            var clip = GetClip(clipName);

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