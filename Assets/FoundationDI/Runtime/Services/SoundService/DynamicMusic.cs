using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 같은 길이의 여러 트랙(레이어)을 동시에 재생하고 레이어별 볼륨을 실시간으로 조절하는 빌더.
    /// <c>ISoundService.CreateDynamicMusic(...)</c>로 생성한다.
    /// </summary>
    public class DynamicMusic
    {
        private readonly ISoundEngine _engine;
        private readonly Dictionary<string, SoundSource> _sourceDictionary = new();
        private readonly Dictionary<string, float> _volumeDictionary = new();

        private SoundSource _referenceSource;

        private float _minHearDistance = 3f;
        private float _maxHearDistance = 500f;
        private AudioRolloffMode _audioRolloffMode = AudioRolloffMode.Logarithmic;
        private AnimationCurve _customVolumeCurve;
        private float _pitch = 1f;
        private float _dopplerLevel = 1f;
        private string _id;
        private Vector3 _position = Vector3.zero;
        private Transform _followTarget;
        private bool _loop;
        private bool _spatialSound;
        private bool _useOcclusion;
        private float _fadeOutTime;
        private AudioClip[] _clips;
        private string[] _cachedTags = Array.Empty<string>();
        private AudioMixerGroup _output;

        private Action _onPlay;
        private Action _onComplete;
        private Action _onLoopCycleComplete;
        private Action _onPause;
        private Action _onPauseComplete;
        private Action _onResume;

        internal DynamicMusic(ISoundEngine engine, string[] tags)
        {
            _engine = engine;

            CacheClipsTags(tags);
        }

        /// <summary>사용 중이면 true. 일시정지 상태도 true다.</summary>
        public bool Using => _referenceSource != null;

        /// <summary>재생 중이면 true.</summary>
        public bool Playing => Using && _referenceSource.Playing;

        /// <summary>일시정지 상태면 true(페이드 아웃 시간은 무시).</summary>
        public bool Paused => Using && _referenceSource.Paused;

        /// <summary>[0,1] 볼륨(기준 레이어 기준).</summary>
        public float Volume => Using
            ? _referenceSource.Volume
            : _volumeDictionary.Count > 0 ? _volumeDictionary.ElementAt(0).Value : 1f;

        /// <summary>피치.</summary>
        public float Pitch => Using ? _referenceSource.Pitch : _pitch;

        /// <summary>재생된 총 시간(초).</summary>
        public float PlayingTime => Using ? _referenceSource.PlayingTime : 0f;

        /// <summary>현재 루프 사이클의 재생 시간(초).</summary>
        public float CurrentLoopCycleTime => Using ? _referenceSource.CurrentLoopCycleTime : 0f;

        /// <summary>완료된 루프 횟수.</summary>
        public int CompletedLoopCycles => Using ? _referenceSource.CompletedLoopCycles : 0;

        /// <summary>클립 길이(초). 모든 레이어가 같은 길이라는 전제로 첫 클립을 사용한다.</summary>
        public float ClipDuration => _clips is { Length: > 0 } && _clips[0] != null ? _clips[0].length : 0f;

        /// <summary>선택된 클립들.</summary>
        public AudioClip[] Clips => _clips;

        /// <summary>재생 전에 모든 레이어의 볼륨을 설정한다.</summary>
        public DynamicMusic SetAllVolumes(float volume)
        {
            if (!TrySetCachedClips()) return this;

            foreach (var tag in _cachedTags)
            {
                _volumeDictionary[tag] = volume;
            }

            return this;
        }

        /// <summary>재생 전에 특정 레이어의 볼륨을 설정한다.</summary>
        public DynamicMusic SetTrackVolume(Track track, float volume) => SetTrackVolume(track.ToString(), volume);

        /// <summary>재생 전에 특정 레이어의 볼륨을 설정한다.</summary>
        public DynamicMusic SetTrackVolume(string tag, float volume)
        {
            if (!TrySetCachedClips()) return this;

            if (_cachedTags.Contains(tag))
            {
                _volumeDictionary[tag] = volume;
            }

            return this;
        }

        /// <summary>3D 음악의 최소/최대 가청 거리.</summary>
        public DynamicMusic SetHearDistance(float minHearDistance, float maxHearDistance)
        {
            _minHearDistance = minHearDistance;
            _maxHearDistance = maxHearDistance;
            return this;
        }

        /// <summary>거리에 따른 볼륨 감쇠 곡선을 미리 정의된 종류로 설정한다.</summary>
        public DynamicMusic SetVolumeRolloffCurve(VolumeRolloffCurve volumeRolloffCurve)
        {
            _audioRolloffMode = volumeRolloffCurve switch
            {
                VolumeRolloffCurve.Logarithmic => AudioRolloffMode.Logarithmic,
                VolumeRolloffCurve.Linear => AudioRolloffMode.Linear,
                _ => AudioRolloffMode.Logarithmic
            };

            return this;
        }

        /// <summary>거리에 따른 볼륨 감쇠를 커스텀 커브로 완전히 제어한다.</summary>
        public DynamicMusic SetCustomVolumeRolloffCurve(AnimationCurve customVolumeCurve)
        {
            _audioRolloffMode = AudioRolloffMode.Custom;
            _customVolumeCurve = customVolumeCurve;
            return this;
        }

        /// <summary>재생 중에 모든 레이어의 볼륨을 바꾼다.</summary>
        public void ChangeAllVolumes(float newVolume, float lerpTime = 0f)
        {
            foreach (var pair in _sourceDictionary)
            {
                if (_volumeDictionary.TryGetValue(pair.Key, out float current) &&
                    Mathf.Approximately(current, newVolume))
                {
                    continue;
                }

                _volumeDictionary[pair.Key] = newVolume;

                if (!Using) return;

                pair.Value.SetVolume(newVolume, lerpTime);
            }
        }

        /// <summary>재생 중에 특정 레이어의 볼륨을 바꾼다.</summary>
        public void ChangeTrackVolume(Track track, float newVolume, float lerpTime = 0f) =>
            ChangeTrackVolume(track.ToString(), newVolume, lerpTime);

        /// <summary>재생 중에 특정 레이어의 볼륨을 바꾼다.</summary>
        public void ChangeTrackVolume(string tag, float newVolume, float lerpTime = 0f)
        {
            if (!_sourceDictionary.TryGetValue(tag, out var source)) return;

            if (_volumeDictionary.TryGetValue(tag, out float current) && Mathf.Approximately(current, newVolume))
            {
                return;
            }

            _volumeDictionary[tag] = newVolume;

            if (!Using) return;

            source.SetVolume(newVolume, lerpTime);
        }

        /// <summary>재생 중에 모든 레이어의 피치를 바꾼다.</summary>
        public void ChangePitch(float newPitch, float lerpTime = 0f)
        {
            if (Mathf.Approximately(_pitch, newPitch)) return;

            _pitch = newPitch;

            if (!Using) return;

            foreach (var pair in _sourceDictionary)
            {
                pair.Value.SetPitch(newPitch, lerpTime);
            }
        }

        /// <summary>피치를 지정한다.</summary>
        public DynamicMusic SetPitch(float pitch)
        {
            _pitch = pitch;
            return this;
        }

        /// <summary>도플러 효과 강도. 0~5, 기본값 1.</summary>
        public DynamicMusic SetDopplerLevel(float dopplerLevel)
        {
            _dopplerLevel = Mathf.Clamp(dopplerLevel, 0f, 5f);
            return this;
        }

        /// <summary>서비스의 id 기반 제어(Stop/Pause/Resume)에 사용할 식별자를 지정한다.</summary>
        public DynamicMusic SetId(string id)
        {
            _id = id;
            return this;
        }

        /// <summary>무한 루프 재생. 멈추려면 <see cref="Stop"/>을 호출한다.</summary>
        public DynamicMusic SetLoop(bool loop = true)
        {
            _loop = loop;
            return this;
        }

        /// <summary>재생 전에 동시에 재생할 트랙들을 지정한다.</summary>
        public DynamicMusic SetClips(params Track[] tracks) =>
            SetClips(tracks.Select(track => track.ToString()).ToArray());

        /// <summary>재생 전에 동시에 재생할 트랙들을 지정한다.</summary>
        public DynamicMusic SetClips(params string[] tracksTags)
        {
            if (!CheckClipsAreValid(tracksTags)) return this;

            _cachedTags = new string[tracksTags.Length];
            Array.Copy(tracksTags, _cachedTags, tracksTags.Length);

            _sourceDictionary.Clear();
            _clips = new AudioClip[tracksTags.Length];

            for (int i = 0; i < tracksTags.Length; i++)
            {
                _clips[i] = _engine.GetTrack(tracksTags[i]);
                _volumeDictionary.TryAdd(tracksTags[i], 1f);
            }

            return this;
        }

        /// <summary>사운드 발생 위치를 지정한다.</summary>
        public DynamicMusic SetPosition(Vector3 position)
        {
            _position = position;
            return this;
        }

        /// <summary>매 프레임 위치를 따라갈 대상을 지정한다.</summary>
        public DynamicMusic SetFollowTarget(Transform followTarget)
        {
            _followTarget = followTarget;
            return this;
        }

        /// <summary>true면 3D, false면 2D(전역) 사운드.</summary>
        public DynamicMusic SetSpatialSound(bool activate = true)
        {
            _spatialSound = activate;
            return this;
        }

        /// <summary>3D 오클루전을 켠다. 켜면 레이캐스트를 위해 자동으로 3D 모드가 된다.</summary>
        public DynamicMusic SetOcclusion(bool activate = true)
        {
            _useOcclusion = activate;

            if (activate)
            {
                _spatialSound = true;
            }

            return this;
        }

        /// <summary>재생이 끝날 때 적용할 페이드 아웃 시간(초).</summary>
        public DynamicMusic SetFadeOut(float fadeOutTime)
        {
            _fadeOutTime = fadeOutTime;
            return this;
        }

        /// <summary>AudioMixer Output을 지정해 볼륨 그룹을 관리한다.</summary>
        public DynamicMusic SetOutput(Output output)
        {
            _output = output.IsNull ? null : _engine.GetOutput(output);
            return this;
        }

        /// <summary>재생 시작 시 호출될 콜백.</summary>
        public DynamicMusic OnPlay(Action onPlay)
        {
            _onPlay = onPlay;
            return this;
        }

        /// <summary>재생 완료 시 호출될 콜백. loop 중이면 수동 Stop 시 호출된다.</summary>
        public DynamicMusic OnComplete(Action onComplete)
        {
            _onComplete = onComplete;
            return this;
        }

        /// <summary>루프 한 바퀴가 끝날 때 호출될 콜백. loop가 true여야 한다.</summary>
        public DynamicMusic OnLoopCycleComplete(Action onLoopCycleComplete)
        {
            _onLoopCycleComplete = onLoopCycleComplete;
            return this;
        }

        /// <summary>일시정지 시 호출될 콜백(페이드 아웃 시간 무시).</summary>
        public DynamicMusic OnPause(Action onPause)
        {
            _onPause = onPause;
            return this;
        }

        /// <summary>일시정지 페이드 아웃까지 끝났을 때 호출될 콜백.</summary>
        public DynamicMusic OnPauseComplete(Action onPauseComplete)
        {
            _onPauseComplete = onPauseComplete;
            return this;
        }

        /// <summary>재개 시 호출될 콜백.</summary>
        public DynamicMusic OnResume(Action onResume)
        {
            _onResume = onResume;
            return this;
        }

        /// <summary>재생한다.</summary>
        /// <param name="fadeInTime">페이드 인 시간(초)</param>
        public void Play(float fadeInTime = 0f)
        {
            if (!TrySetCachedClips()) return;

            if (Using && Playing)
            {
                Stop();
            }

            for (int i = 0; i < _cachedTags.Length; i++)
            {
                string tag = _cachedTags[i];

                var source = _engine.GetSource();

                _sourceDictionary[tag] = source;

                source
                    .SetVolume(_volumeDictionary[tag])
                    .SetHearDistance(_minHearDistance, _maxHearDistance)
                    .SetVolumeRolloffCurve(_audioRolloffMode, _customVolumeCurve)
                    .SetPitch(_pitch)
                    .SetDopplerLevel(_dopplerLevel)
                    .SetLoop(_loop)
                    .SetClip(_clips[i])
                    .SetPosition(_position)
                    .SetFollowTarget(_followTarget)
                    .SetSpatialSound(_spatialSound)
                    .SetOcclusion(_useOcclusion)
                    .SetFadeOut(_fadeOutTime)
                    .SetId(_id)
                    .SetOutput(_output);

                // 콜백은 기준 레이어에서만 한 번 발생시킨다.
                if (i == 0)
                {
                    _referenceSource = source;

                    source
                        .OnPlay(_onPlay)
                        .OnComplete(_onComplete)
                        .OnLoopCycleComplete(_onLoopCycleComplete)
                        .OnPause(_onPause)
                        .OnPauseComplete(_onPauseComplete)
                        .OnResume(_onResume);
                }

                source.Play(fadeInTime);
            }
        }

        /// <summary>일시정지한다.</summary>
        public void Pause(float fadeOutTime = 0f)
        {
            if (!Using) return;

            foreach (var source in _sourceDictionary.Values)
            {
                source.Pause(fadeOutTime);
            }
        }

        /// <summary>재개한다.</summary>
        public void Resume(float fadeInTime = 0f)
        {
            if (!Using) return;

            foreach (var source in _sourceDictionary.Values)
            {
                source.Resume(fadeInTime);
            }
        }

        /// <summary>정지한다.</summary>
        public void Stop(float fadeOutTime = 0f)
        {
            if (!Using) return;

            var sources = _sourceDictionary.Values.ToArray();

            for (int i = 0; i < sources.Length; i++)
            {
                if (i < sources.Length - 1)
                {
                    sources[i].Stop(fadeOutTime);
                    continue;
                }

                sources[i].Stop(fadeOutTime, () =>
                {
                    _referenceSource = null;
                    _sourceDictionary.Clear();
                });
            }
        }

        private void CacheClipsTags(string[] tracks)
        {
            if (tracks == null || !CheckClipsAreValid(tracks)) return;

            _cachedTags = new string[tracks.Length];

            Array.Copy(tracks, _cachedTags, tracks.Length);
        }

        private bool TrySetCachedClips()
        {
            if (_clips is { Length: > 0 }) return true;

            if (!CheckClipsAreValid(_cachedTags)) return false;

            SetClips(_cachedTags);

            return true;
        }

        private static bool CheckClipsAreValid(string[] tracks)
        {
            bool anyTrackIsNullOrEmpty = tracks.Any(t => string.IsNullOrEmpty(t) || string.Equals(t, Track.NULL_TAG));
            bool anyTrackIsDuplicated = HasDuplicates(tracks);

            if (tracks.Length > 0 && !anyTrackIsNullOrEmpty && !anyTrackIsDuplicated) return true;

            Debug.LogError("[SoundService] 다이내믹 뮤직에 지정한 트랙 중 비어 있거나 null이거나 중복된 항목이 있습니다.");

            return false;
        }

        private static bool HasDuplicates<T>(IEnumerable<T> items)
        {
            var seen = new HashSet<T>();

            return items.Any(item => !seen.Add(item));
        }
    }
}
