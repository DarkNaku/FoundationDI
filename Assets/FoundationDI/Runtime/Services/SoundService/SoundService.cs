using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// <see cref="ISoundService"/>의 기본 구현.
    /// 소스 풀링·Output 볼륨 관리·오클루전 계산을 인스턴스 상태로 들고 있다.
    /// </summary>
    public class SoundService : ISoundService, ISoundEngine
    {
        private readonly List<SoundSource> _sourcePool = new();
        private readonly ISoundVolumeStorage _volumeStorage;

        private GameObject _poolParent;
        private AudioListener _cachedListener;
        private bool _disposed;

        public SoundServiceSettings Settings { get; }

        public SoundService(SoundServiceSettings settings) : this(settings, new PlayerPrefsVolumeStorage())
        {
        }

        public SoundService(SoundServiceSettings settings, ISoundVolumeStorage volumeStorage)
        {
            Settings = settings;
            _volumeStorage = volumeStorage;

            if (Settings == null)
            {
                Debug.LogError("[SoundService] SoundServiceSettings가 주입되지 않았습니다.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            StopAll();

            _sourcePool.Clear();
            _cachedListener = null;

            // 플레이모드 종료 시 Unity의 오브젝트 파괴와 Container.Dispose 순서가 보장되지 않는다.
            // fake-null 가드로 이미 파괴된 경우를 건너뛴다.
            if (_poolParent == null) return;

            if (Application.isPlaying)
            {
                Object.Destroy(_poolParent);
            }
            else
            {
                Object.DestroyImmediate(_poolParent);
            }

            _poolParent = null;
        }

        public Sound CreateSound(SFX sfx) => new(this, sfx.ToString());

        public Sound CreateSound(string tag) => new(this, tag);

        public Music CreateMusic(Track track) => new(this, track.ToString());

        public Music CreateMusic(string tag) => new(this, tag);

        public Playlist CreatePlaylist(params Track[] tracks) =>
            new(this, tracks.Select(track => track.ToString()).ToArray());

        public Playlist CreatePlaylist(params string[] tags) => new(this, tags);

        public DynamicMusic CreateDynamicMusic(params Track[] tracks) =>
            new(this, tracks.Select(track => track.ToString()).ToArray());

        public DynamicMusic CreateDynamicMusic(params string[] tags) => new(this, tags);

        public void ChangeOutputVolume(Output output, float value) => ChangeOutputVolume(output.ToString(), value);

        public void ChangeOutputVolume(string outputName, float value)
        {
            var outputGroup = GetOutputGroup(outputName);

            if (outputGroup == null || outputGroup.audioMixer == null)
            {
                Debug.LogError($"[SoundService] '{outputName}' Output이 없어 볼륨을 바꿀 수 없습니다. " +
                               "Output Manager 창에서 Output 데이터베이스를 갱신했는지 확인하세요.");
                return;
            }

            if (Application.isPlaying)
            {
                SetAudioMixerLinearVolume(outputGroup.audioMixer, outputName, value);
            }

            _volumeStorage.SetFloat(outputName, value);
            _volumeStorage.Save();
        }

        public float GetSavedOutputVolume(string outputName)
        {
            if (_volumeStorage.HasKey(outputName)) return _volumeStorage.GetFloat(outputName, 1f);

            _volumeStorage.SetFloat(outputName, 1f);

            return 1f;
        }

        public void PauseAll(float fadeOutTime = 0f)
        {
            foreach (var source in _sourcePool)
            {
                source.Pause(fadeOutTime);
            }
        }

        public void Pause(string id, float fadeOutTime = 0f)
        {
            var source = _sourcePool.FirstOrDefault(element => element.Id == id);

            if (source == null || !source.Using)
            {
                Debug.LogWarning($"[SoundService] id가 '{id}'인 재생이 없습니다.");
                return;
            }

            source.Pause(fadeOutTime);
        }

        public void StopAll(float fadeOutTime = 0f)
        {
            foreach (var source in _sourcePool)
            {
                source.Stop(fadeOutTime);
            }
        }

        public void Stop(string id, float fadeOutTime = 0f)
        {
            var source = _sourcePool.FirstOrDefault(element => element.Id == id);

            if (source == null || !source.Using)
            {
                Debug.LogWarning($"[SoundService] id가 '{id}'인 재생이 없습니다.");
                return;
            }

            source.Stop(fadeOutTime);
        }

        public void ResumeAll(float fadeInTime = 0f)
        {
            foreach (var source in _sourcePool)
            {
                if (source.Paused)
                {
                    source.Resume(fadeInTime);
                }
            }
        }

        public void Resume(string id, float fadeInTime = 0f)
        {
            var source = _sourcePool.FirstOrDefault(element => element.Id == id);

            if (source == null || !source.Paused)
            {
                Debug.LogWarning($"[SoundService] id가 '{id}'인 일시정지된 재생이 없습니다.");
                return;
            }

            source.Resume(fadeInTime);
        }

        SoundSource ISoundEngine.GetSource()
        {
            if (_poolParent == null || !_poolParent.activeInHierarchy)
            {
                _poolParent = new GameObject("[SoundService] Sources Pool");
                _sourcePool.Clear();

                if (Application.isPlaying)
                {
                    Object.DontDestroyOnLoad(_poolParent);
                }
            }

            if (_sourcePool.Count != _poolParent.transform.childCount)
            {
                _sourcePool.Clear();
            }

            foreach (var element in _sourcePool)
            {
                if (!element.Using) return element;
            }

            var instance = new GameObject($"Audio Source {_sourcePool.Count}");
            instance.transform.SetParent(_poolParent.transform);

            var audioSource = instance.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.dopplerLevel = 0f;

            var soundSource = instance.AddComponent<SoundSource>().Init(this, audioSource);

            _sourcePool.Add(soundSource);

            return soundSource;
        }

        AudioMixerGroup ISoundEngine.GetOutput(Output output) => GetOutputGroupWithSavedVolume(output.ToString());

        AudioMixerGroup ISoundEngine.GetOutput(string outputName) => GetOutputGroupWithSavedVolume(outputName);

        AudioClip ISoundEngine.GetSFX(string tag, int index)
        {
            var collection = Settings != null ? Settings.SoundDataCollection : null;

            if (collection == null) return null;

            var soundData = collection.GetSound(tag);

            return soundData?.GetClip(index);
        }

        AudioClip ISoundEngine.GetTrack(string tag, int index)
        {
            var collection = Settings != null ? Settings.MusicDataCollection : null;

            if (collection == null) return null;

            var soundData = collection.GetMusicTrack(tag);

            return soundData?.GetClip(index);
        }

        bool ISoundEngine.TryGetListener(out AudioListener listener)
        {
            if (_cachedListener == null)
            {
                _cachedListener = Object.FindAnyObjectByType<AudioListener>(FindObjectsInactive.Include);
            }

            listener = _cachedListener;

            return listener != null;
        }

        void ISoundEngine.CalculateOcclusion(Vector3 listenerPosition, Vector3 sourcePosition, out float factor)
        {
            factor = 0f;

            var toSource = sourcePosition - listenerPosition;
            float distance = toSource.magnitude;

            if (distance <= 0.1f || distance > Settings.MaxDistance) return;

            var direction = toSource / distance;

            var axis1 = Vector3.Cross(direction, Vector3.up);

            if (axis1.sqrMagnitude < 0.0001f)
            {
                axis1 = Vector3.Cross(direction, Vector3.right);
            }

            axis1.Normalize();

            var axis2 = Vector3.Cross(direction, axis1).normalized;

            const float minRadius = 0.3f;
            float radius = Mathf.Max(minRadius, distance * 0.15f);

            var directOrigins = new[]
            {
                listenerPosition,
                listenerPosition + axis1 * radius,
                listenerPosition - axis1 * radius,
                listenerPosition + axis2 * radius,
                listenerPosition - axis2 * radius,
                listenerPosition + (axis1 + axis2).normalized * radius,
                listenerPosition + (axis1 - axis2).normalized * radius
            };

            int totalDirectRays = directOrigins.Length;
            int blockedDirectRays = 0;

            foreach (var origin in directOrigins)
            {
                var rayDirection = (sourcePosition - origin).normalized;
                float rayDistance = Vector3.Distance(origin, sourcePosition);

                if (rayDistance <= 0.05f) continue;

                if (Physics.Raycast(origin, rayDirection, rayDistance, Settings.OcclusionLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    blockedDirectRays++;
                }
            }

            float directBlockedRatio = (float)blockedDirectRays / Mathf.Max(1, totalDirectRays);

            if (directBlockedRatio <= 0.1f)
            {
                factor = 0f;
                return;
            }

            if (directBlockedRatio < 0.9f || Settings.MaxBounces <= 0)
            {
                factor = Mathf.Clamp01(directBlockedRatio);
                return;
            }

            float bounceRadius = Mathf.Max(Settings.BounceRadiusMin, distance * 0.3f);

            int raysPerCircle = Mathf.Max(Settings.BounceRaysPerCircle, 4);
            int unblockedBounceRays = 0;
            int totalBounceRays = raysPerCircle;

            for (int i = 0; i < raysPerCircle; i++)
            {
                float angle = Mathf.PI * 2f * i / raysPerCircle;

                var offset = (Mathf.Cos(angle) * axis1 + Mathf.Sin(angle) * axis2) * bounceRadius;
                var origin = listenerPosition + offset;

                var rayDirection = (sourcePosition - origin).normalized;
                float rayDistance = Vector3.Distance(origin, sourcePosition);

                if (rayDistance <= 0.05f)
                {
                    totalBounceRays--;
                    continue;
                }

                if (!Physics.Raycast(origin, rayDirection, rayDistance, Settings.OcclusionLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    unblockedBounceRays++;
                }
            }

            if (totalBounceRays <= 0)
            {
                factor = 1f;
                return;
            }

            float bounceUnblockedRatio = (float)unblockedBounceRays / totalBounceRays;

            if (bounceUnblockedRatio <= 0f)
            {
                factor = 1f;
            }
            else
            {
                factor = Mathf.Clamp01(1f - 0.5f * bounceUnblockedRatio);
            }
        }

        private AudioMixerGroup GetOutputGroup(string outputName)
        {
            var collection = Settings != null ? Settings.OutputDataCollection : null;

            return collection == null ? null : collection.GetOutput(outputName);
        }

        private AudioMixerGroup GetOutputGroupWithSavedVolume(string outputName)
        {
            var audioMixerGroup = GetOutputGroup(outputName);

            if (audioMixerGroup == null || audioMixerGroup.audioMixer == null) return audioMixerGroup;

            SetAudioMixerLinearVolume(audioMixerGroup.audioMixer, outputName, GetSavedOutputVolume(outputName));

            return audioMixerGroup;
        }

        private static void SetAudioMixerLinearVolume(AudioMixer audioMixer, string volumeParameterName, float volume)
        {
            audioMixer.SetFloat(volumeParameterName, Mathf.Log10(Mathf.Clamp(volume, 0.001f, 0.99f)) * 20f);
        }
    }
}
