using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;
using R3;
using VContainer;

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
        
        private readonly ISoundCatalog _catalog;
        private readonly Transform _root;
        private AudioSource _bgmPlayer;
        private HashSet<AudioSource> _sfxPlayers = new();
        private HashSet<string> _playedClipInThisFrame = new();
        private IDisposable _disposable;

        public SoundService(ISoundCatalog catalog)
        {
            _catalog = catalog;

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
            _disposable?.Dispose();
            _disposable = null;

            // 플레이모드 종료 시 Unity의 오브젝트 파괴와 Container.Dispose 순서가 보장되지 않는다.
            // _root가 먼저 파괴되면 _root.gameObject 접근에서 MissingReferenceException이 나므로
            // fake-null 가드로 이미 파괴된 경우를 건너뛴다.
            if (_root != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_root.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(_root.gameObject);
                }
            }
        }

        public void Play(string key)
        {
            if (Mathf.Approximately(VolumeSFX, 0f) || !SFXEnabled) return;
            if (_playedClipInThisFrame.Contains(key)) return;

            if (!_catalog.TryGetClip(key, out var clip) || clip == null)
            {
                Debug.LogError($"[SoundService] Play : Clip not found in catalog. ({key})");
                return;
            }

            var player = GetPlayer();
            player.clip = clip;
            player.loop = false;
            player.volume = VolumeSFX;
            player.Play();

            _playedClipInThisFrame.Add(key);
        }

        public void PlayBGM(string key)
        {
            if (Mathf.Approximately(VolumeBGM, 0f) || !BGMEnabled) return;

            if (!_catalog.TryGetClip(key, out var clip) || clip == null)
            {
                Debug.LogError($"[SoundService] PlayBGM : Clip not found in catalog. ({key})");
                return;
            }

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
            var clips = _catalog.PreloadClips;
            if (clips == null) return;

            var tasks = new List<UniTask>();

            foreach (var clip in clips)
            {
                if (clip == null) continue;
                tasks.Add(LoadAudioDataAsync(clip));
            }

            await UniTask.WhenAll(tasks);
        }

        private static async UniTask LoadAudioDataAsync(AudioClip clip)
        {
            // 임포트 설정 "Load In Background"가 켜진 압축 클립만 실제 비동기 로드된다.
            // 이미 로드된(절차적 클립 포함) 경우 즉시 반환한다.
            if (clip.loadState == AudioDataLoadState.Loaded) return;

            clip.LoadAudioData();
            await UniTask.WaitWhile(() => clip.loadState == AudioDataLoadState.Loading);
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
                // _root가 DontDestroyOnLoad라 자식은 자동으로 따라간다.
                // 비-root에 DontDestroyOnLoad를 걸면 경고만 나고 무시되므로 걸지 않는다(BGM Player와 동일).
                player.transform.parent = _root;

                _sfxPlayers.Add(player);
            }

            return player;
        }
    }

    public static class SoundServiceVContainerExtensions
    {
        /// <summary>
        /// SoundService를 컨테이너에 등록한다.
        /// SoundService는 등록된 <see cref="ISoundCatalog"/>에서 클립을 직접 가져와 재생하므로
        /// <see cref="IResourceService"/> 등록은 필요하지 않다.
        /// </summary>
        public static void RegisterSoundService(this IContainerBuilder builder, SoundCatalogSO catalog)
        {
            builder.RegisterInstance<ISoundCatalog>(catalog);
            builder.Register<ISoundService, SoundService>(Lifetime.Singleton);
        }
    }
}