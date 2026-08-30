using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 여러 트랙을 순서대로 이어 재생하는 빌더.
    /// <c>ISoundService.CreatePlaylist(...)</c>로 생성한다.
    /// </summary>
    public class Playlist
    {
        private readonly ISoundEngine _engine;

        private SoundSource _source;

        private float _volume = 1f;
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
        private float _fadeInTime;
        private readonly Queue<AudioClip> _playlist = new();
        private readonly Queue<string> _cachedTags = new();
        private AudioMixerGroup _output;

        private Action _onPlay;
        private Action _onComplete;
        private Action _onLoopCycleComplete;
        private Action _onNextTrackStart;
        private Action _onPause;
        private Action _onPauseComplete;
        private Action _onResume;

        internal Playlist(ISoundEngine engine, string[] tags)
        {
            _engine = engine;
            _output = engine.GetOutput(Output.Null);   // SetOutput을 부르지 않아도 기본 Output을 탄다

            if (tags == null) return;

            foreach (var tag in tags)
            {
                _cachedTags.Enqueue(tag);
            }
        }

        /// <summary>사용 중이면 true. 일시정지 상태도 true다.</summary>
        public bool Using => _source != null;

        /// <summary>재생 중이면 true.</summary>
        public bool Playing => Using && _source.Playing;

        /// <summary>일시정지 상태면 true(페이드 아웃 시간은 무시).</summary>
        public bool Paused => Using && _source.Paused;

        /// <summary>[0,1] 볼륨.</summary>
        public float Volume => Using ? _source.Volume : _volume;

        /// <summary>피치.</summary>
        public float Pitch => Using ? _source.Pitch : _pitch;

        /// <summary>재생된 총 시간(초).</summary>
        public float PlayingTime => Using ? _source.PlayingTime : 0f;

        /// <summary>현재 루프 사이클의 재생 시간(초).</summary>
        public float CurrentLoopCycleTime => Using ? _source.CurrentLoopCycleTime : 0f;

        /// <summary>완료된 루프 횟수.</summary>
        public int CompletedLoopCycles => Using ? _source.CompletedLoopCycles : 0;

        /// <summary>현재 재생 중인 클립의 길이(초).</summary>
        public float CurrentClipDuration => Using ? _source.CurrentClipDuration : 0f;

        /// <summary>플레이리스트 전체 길이(초).</summary>
        public float PlayListDuration
        {
            get
            {
                float duration = 0f;

                foreach (var clip in _playlist)
                {
                    if (clip == null) continue;

                    duration += clip.length;
                }

                return duration;
            }
        }

        /// <summary>지금까지 재생된 트랙 수.</summary>
        public float ReproducedTracks => Using ? _source.ReproducedTracks : 0;

        /// <summary>현재 재생 중인 클립.</summary>
        public AudioClip CurrentPlaylistClip => Using ? _source.CurrentClip : null;

        /// <summary>다음에 재생될 클립.</summary>
        public AudioClip NextPlaylistClip => Using ? _source.NextPlaylistClip : null;

        /// <summary>재생 순서대로 나열된 태그 목록.</summary>
        public string[] PlaylistClipsTags => _cachedTags.ToArray();

        /// <summary>재생 전에 볼륨을 설정한다.</summary>
        public Playlist SetVolume(float volume)
        {
            _volume = volume;
            return this;
        }

        /// <summary>3D 음악의 최소/최대 가청 거리.</summary>
        public Playlist SetHearDistance(float minHearDistance, float maxHearDistance)
        {
            _minHearDistance = minHearDistance;
            _maxHearDistance = maxHearDistance;
            return this;
        }

        /// <summary>거리에 따른 볼륨 감쇠 곡선을 미리 정의된 종류로 설정한다.</summary>
        public Playlist SetVolumeRolloffCurve(VolumeRolloffCurve volumeRolloffCurve)
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
        public Playlist SetCustomVolumeRolloffCurve(AnimationCurve customVolumeCurve)
        {
            _audioRolloffMode = AudioRolloffMode.Custom;
            _customVolumeCurve = customVolumeCurve;
            return this;
        }

        /// <summary>재생 중에 볼륨을 바꾼다.</summary>
        public void ChangeVolume(float newVolume, float lerpTime = 0f)
        {
            if (Mathf.Approximately(_volume, newVolume)) return;

            _volume = newVolume;

            if (!Using) return;

            _source.SetVolume(newVolume, lerpTime);
        }

        /// <summary>재생 중에 피치를 바꾼다.</summary>
        public void ChangePitch(float newPitch, float lerpTime = 0f)
        {
            if (Mathf.Approximately(_pitch, newPitch)) return;

            _pitch = newPitch;

            if (!Using) return;

            _source.SetPitch(newPitch, lerpTime);
        }

        /// <summary>피치를 지정한다.</summary>
        public Playlist SetPitch(float pitch)
        {
            _pitch = pitch;
            return this;
        }

        /// <summary>도플러 효과 강도. 0~5, 기본값 1.</summary>
        public Playlist SetDopplerLevel(float dopplerLevel)
        {
            _dopplerLevel = Mathf.Clamp(dopplerLevel, 0f, 5f);
            return this;
        }

        /// <summary>서비스의 id 기반 제어(Stop/Pause/Resume)에 사용할 식별자를 지정한다.</summary>
        public Playlist SetId(string id)
        {
            _id = id;
            return this;
        }

        /// <summary>무한 루프 재생. 멈추려면 <see cref="Stop"/>을 호출한다.</summary>
        public Playlist SetLoop(bool loop = true)
        {
            _loop = loop;
            return this;
        }

        /// <summary>재생 전에 새 플레이리스트를 지정한다.</summary>
        public Playlist SetPlaylist(params Track[] playlistTracks) =>
            SetPlaylist(playlistTracks.Select(track => track.ToString()).ToArray());

        /// <summary>재생 전에 새 플레이리스트를 지정한다.</summary>
        public Playlist SetPlaylist(params string[] playlistTags)
        {
            if (!CheckClipsAreValid(playlistTags)) return this;

            _playlist.Clear();
            _cachedTags.Clear();

            foreach (var tag in playlistTags)
            {
                _cachedTags.Enqueue(tag);
                _playlist.Enqueue(_engine.GetTrack(tag));
            }

            return this;
        }

        /// <summary>기존 플레이리스트 끝에 트랙을 추가한다.</summary>
        public void AddToPlaylist(Track addedTrack) => AddToPlaylist(addedTrack.ToString());

        /// <summary>기존 플레이리스트 끝에 트랙을 추가한다.</summary>
        public void AddToPlaylist(string addedTrackTag)
        {
            var clip = _engine.GetTrack(addedTrackTag);

            if (clip == null) return;

            _cachedTags.Enqueue(addedTrackTag);
            _playlist.Enqueue(clip);

            if (!Using) return;

            _source.AddToPlaylist(clip);
        }

        /// <summary>플레이리스트 순서를 무작위로 섞는다.</summary>
        public void Shuffle()
        {
            if (!TrySetCachedClips()) return;

            _cachedTags.Shuffle();

            SetPlaylist(_cachedTags.ToArray());

            if (!Using) return;

            _source.SetPlaylist(_playlist);
        }

        /// <summary>사운드 발생 위치를 지정한다.</summary>
        public Playlist SetPosition(Vector3 position)
        {
            _position = position;
            return this;
        }

        /// <summary>매 프레임 위치를 따라갈 대상을 지정한다.</summary>
        public Playlist SetFollowTarget(Transform followTarget)
        {
            _followTarget = followTarget;
            return this;
        }

        /// <summary>true면 3D, false면 2D(전역) 사운드.</summary>
        public Playlist SetSpatialSound(bool activate = true)
        {
            _spatialSound = activate;
            return this;
        }

        /// <summary>3D 오클루전을 켠다. 켜면 레이캐스트를 위해 자동으로 3D 모드가 된다.</summary>
        public Playlist SetOcclusion(bool activate = true)
        {
            _useOcclusion = activate;

            if (activate)
            {
                _spatialSound = true;
            }

            return this;
        }

        /// <summary>모든 트랙에 적용할 페이드 아웃 시간(초).</summary>
        public Playlist SetFadeOut(float fadeOutTime)
        {
            _fadeOutTime = fadeOutTime;
            return this;
        }

        /// <summary>모든 트랙에 적용할 페이드 인 시간(초).</summary>
        public Playlist SetFadeIn(float fadeInTime)
        {
            _fadeInTime = fadeInTime;
            return this;
        }

        /// <summary>AudioMixer Output을 지정해 볼륨 그룹을 관리한다.</summary>
        public Playlist SetOutput(Output output)
        {
            _output = _engine.GetOutput(output);   // 비어 있으면 엔진이 기본 Output으로 해석한다
            return this;
        }

        /// <summary>플레이리스트 시작 시 호출될 콜백.</summary>
        public Playlist OnPlay(Action onPlay)
        {
            _onPlay = onPlay;
            return this;
        }

        /// <summary>플레이리스트 완료 시 호출될 콜백. loop 중이면 수동 Stop 시 호출된다.</summary>
        public Playlist OnComplete(Action onComplete)
        {
            _onComplete = onComplete;
            return this;
        }

        /// <summary>루프 한 바퀴가 끝날 때 호출될 콜백. loop가 true여야 한다.</summary>
        public Playlist OnLoopCycleComplete(Action onLoopCycleComplete)
        {
            _onLoopCycleComplete = onLoopCycleComplete;
            return this;
        }

        /// <summary>다음 트랙이 시작될 때 호출될 콜백.</summary>
        public Playlist OnNextTrackStart(Action onNextTrackStart)
        {
            _onNextTrackStart = onNextTrackStart;
            return this;
        }

        /// <summary>일시정지 시 호출될 콜백(페이드 아웃 시간 무시).</summary>
        public Playlist OnPause(Action onPause)
        {
            _onPause = onPause;
            return this;
        }

        /// <summary>일시정지 페이드 아웃까지 끝났을 때 호출될 콜백.</summary>
        public Playlist OnPauseComplete(Action onPauseComplete)
        {
            _onPauseComplete = onPauseComplete;
            return this;
        }

        /// <summary>재개 시 호출될 콜백.</summary>
        public Playlist OnResume(Action onResume)
        {
            _onResume = onResume;
            return this;
        }

        /// <summary>플레이리스트를 재생한다.</summary>
        public void Play()
        {
            if (!TrySetCachedClips()) return;

            if (Using && Playing)
            {
                Stop();
            }

            _source = _engine.GetSource();
            _source
                .MarkAsPlaylist()
                .SetVolume(_volume)
                .SetHearDistance(_minHearDistance, _maxHearDistance)
                .SetVolumeRolloffCurve(_audioRolloffMode, _customVolumeCurve)
                .SetPitch(_pitch)
                .SetDopplerLevel(_dopplerLevel)
                .SetLoop(_loop)
                .SetPlaylist(_playlist)
                .SetPosition(_position)
                .SetFollowTarget(_followTarget)
                .SetSpatialSound(_spatialSound)
                .SetOcclusion(_useOcclusion)
                .SetFadeIn(_fadeInTime)
                .SetFadeOut(_fadeOutTime)
                .SetId(_id)
                .SetOutput(_output)
                .OnPlay(_onPlay)
                .OnComplete(_onComplete)
                .OnLoopCycleComplete(_onLoopCycleComplete)
                .OnNextTrackStart(_onNextTrackStart)
                .OnPause(_onPause)
                .OnPauseComplete(_onPauseComplete)
                .OnResume(_onResume)
                .PlayPlaylist(_fadeInTime);
        }

        /// <summary>일시정지한다.</summary>
        public void Pause(float fadeOutTime = 0f)
        {
            if (!Using) return;

            _source.Pause(fadeOutTime);
        }

        /// <summary>재개한다.</summary>
        public void Resume(float fadeInTime = 0f)
        {
            if (!Using) return;

            _source.Resume(fadeInTime);
        }

        /// <summary>정지한다.</summary>
        public void Stop(float fadeOutTime = 0f)
        {
            if (!Using) return;

            _source.Stop(fadeOutTime, () => _source = null);
        }

        private bool TrySetCachedClips()
        {
            if (_playlist.Count > 0) return true;

            if (!CheckClipsAreValid(_cachedTags.ToArray())) return false;

            SetPlaylist(_cachedTags.ToArray());

            return true;
        }

        private static bool CheckClipsAreValid(string[] tracks)
        {
            bool anyTrackIsNullOrEmpty = tracks.Any(t => string.IsNullOrEmpty(t) || string.Equals(t, Track.NULL_TAG));

            if (tracks.Length > 0 && !anyTrackIsNullOrEmpty) return true;

            Debug.LogError("[SoundService] 플레이리스트에 지정한 음악 트랙 중 비어 있거나 null인 항목이 있습니다.");

            return false;
        }
    }
}
