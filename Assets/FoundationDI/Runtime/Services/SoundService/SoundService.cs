using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using R3;
using UnityEngine.AddressableAssets;
using UnityEngine.Pool;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DarkNaku.FoundationDI
{
    public interface ISoundService : IDisposable
    {
        bool IsSFXEnabled { get; set; }
        bool IsBGMEnabled { get; set; }
        bool IsPlayingBGM { get; }
        float VolumeSFX { get; set; }
        float VolumeBGM { get; set; }
        void Play(string clipName);
        void PlayBGM(string clipName);
        void StopBGM();
    }
    
    public class SoundService : ISoundService
    {
        public bool IsSFXEnabled { get; set; }
        public bool IsBGMEnabled { get; set; }
        public bool IsPlayingBGM { get; }
        
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
        
        private const string SFX_VOLUME = "SFX_VOLUME";
        private const string BGM_VOLUME = "BGM_VOLUME";
        
        private readonly Transform _root;
        private readonly Dictionary<string, SoundData> _table;
        private AudioSource _bgmPlayer;
        private HashSet<AudioSource> _sfxPlayers = new();
        private HashSet<string> _playedClipInThisFrame = new();
        private IDisposable _disposable;

        public SoundService()
        {
            _table = new Dictionary<string, SoundData>();
            
            var root = new GameObject("[SoundService]");
            
            _root = root.transform;
            
            Object.DontDestroyOnLoad(root);
            
            _bgmPlayer = new GameObject("BGM Player").AddComponent<AudioSource>();
            _bgmPlayer.transform.parent = _root;

            _disposable = Observable.EveryUpdate(UnityFrameProvider.PostLateUpdate).Subscribe(OnPostLateUpdate);
        }
        
        public void Dispose()
        {
            _disposable.Dispose();
            
            Object.Destroy(_root.gameObject);
        }
        
        public void Play(string clipName)
        {
            if (Mathf.Approximately(VolumeSFX, 0f) || !IsSFXEnabled) return;
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
            if (Mathf.Approximately(VolumeBGM, 0f) || !IsBGMEnabled) return;

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
                Object.DontDestroyOnLoad(player.gameObject);
                _sfxPlayers.Add(player);
            }

            return player;
        }

        private AudioClip GetClip(string key) 
        {
            if (_table.TryGetValue(key, out var data)) 
            {
                return data.Clip;
            } 
            else
            {
                return Load(key)?.Clip;
            }
        }

        private SoundData Load(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[SoundService] Load : Key is wrong.");
                return null;
            }
            
            var clip = Resources.Load<AudioClip>(key);

            if (clip == null)
            {
                try
                {
                    var handle = Addressables.LoadAssetAsync<AudioClip>(key);
                    clip = handle.WaitForCompletion();
                    return Register(key, clip, handle);
                }
                catch (InvalidKeyException e)
                {
                    Debug.LogError($"[PoolService] Load : {e.Message}");
                    return null;
                }
            }

            return Register(key, clip, default);
        }
        
        private SoundData Register(string key, AudioClip clip, AsyncOperationHandle<AudioClip> handle)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[SoundService] Register : Key is wrong.");
                return null;
            }

            if (clip == null)
            {
                Debug.LogError($"[SoundService] Register : Prefab is null.");
                return null;
            }

            var data = new SoundData(clip, handle);
            
            _table.TryAdd(key, data);

            return data;
        }
    }
}