using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 음악 재생 빌더. <c>ISoundService.CreateMusic(...)</c>로 생성하고 체이닝으로 설정한 뒤
    /// <see cref="Play"/>를 호출한다.
    /// </summary>
    public class Music
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
        private bool _randomClip = true;
        private int _clipIndex = -1;
        private bool _forgetSourceOnStop;
        private AudioClip _clip;
        private AudioMixerGroup _output;
        private string _cachedTag;

        private Action _onPlay;
        private Action _onComplete;
        private Action _onLoopCycleComplete;
        private Action _onPause;
        private Action _onPauseComplete;
        private Action _onResume;

        internal Music(ISoundEngine engine, string tag)
        {
            _engine = engine;
            _cachedTag = tag;
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

        /// <summary>클립 배열 인덱스. 특정 클립을 지정하지 않았으면 -1.</summary>
        public int ClipIndex => _clipIndex;

        /// <summary>재생된 총 시간(초).</summary>
        public float PlayingTime => Using ? _source.PlayingTime : 0f;

        /// <summary>현재 루프 사이클의 재생 시간(초).</summary>
        public float CurrentLoopCycleTime => Using ? _source.CurrentLoopCycleTime : 0f;

        /// <summary>완료된 루프 횟수.</summary>
        public int CompletedLoopCycles => Using ? _source.CompletedLoopCycles : 0;

        /// <summary>선택된 클립의 길이(초).</summary>
        public float ClipDuration => _clip != null ? _clip.length : 0f;

        /// <summary>선택된 클립.</summary>
        public AudioClip Clip => _clip;

        /// <summary>재생 전에 볼륨을 설정한다.</summary>
        public Music SetVolume(float volume)
        {
            _volume = volume;
            return this;
        }

        /// <summary>3D 음악의 최소/최대 가청 거리.</summary>
        public Music SetHearDistance(float minHearDistance, float maxHearDistance)
        {
            _minHearDistance = minHearDistance;
            _maxHearDistance = maxHearDistance;
            return this;
        }

        /// <summary>거리에 따른 볼륨 감쇠 곡선을 미리 정의된 종류로 설정한다.</summary>
        public Music SetVolumeRolloffCurve(VolumeRolloffCurve volumeRolloffCurve)
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
        public Music SetCustomVolumeRolloffCurve(AnimationCurve customVolumeCurve)
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
        public Music SetPitch(float pitch)
        {
            _pitch = pitch;
            return this;
        }

        /// <summary>도플러 효과 강도. 0~5, 기본값 1.</summary>
        public Music SetDopplerLevel(float dopplerLevel)
        {
            _dopplerLevel = Mathf.Clamp(dopplerLevel, 0f, 5f);
            return this;
        }

        /// <summary>서비스의 id 기반 제어(Stop/Pause/Resume)에 사용할 식별자를 지정한다.</summary>
        public Music SetId(string id)
        {
            _id = id;
            return this;
        }

        /// <summary>무한 루프 재생. 멈추려면 <see cref="Stop"/>을 호출한다.</summary>
        public Music SetLoop(bool loop = true)
        {
            _loop = loop;
            return this;
        }

        /// <summary>클립을 태그로 교체한다.</summary>
        public Music SetClip(string tag)
        {
            _cachedTag = tag;

            if (!string.Equals(tag, Track.NULL_TAG))
            {
                _clip = _engine.GetTrack(tag);
            }

            return this;
        }

        /// <summary>재생 전에 트랙을 교체한다.</summary>
        public Music SetClip(Track track) => SetClip(track.ToString());

        /// <summary>Play()마다 같은 태그의 클립 중 하나를 무작위로 고를지 정한다.</summary>
        public Music SetRandomClip(bool random = true)
        {
            _randomClip = random;
            return this;
        }

        /// <summary>같은 태그에 등록된 클립 중 인덱스로 하나를 고정한다.</summary>
        public Music SetClipByIndex(int index)
        {
            if (index < 0)
            {
                Debug.LogWarning("[SoundService] 클립 인덱스는 0보다 작을 수 없습니다.");
                return this;
            }

            if (string.IsNullOrEmpty(_cachedTag))
            {
                Debug.LogWarning("[SoundService] 클립을 고르기 전에 음악 태그를 먼저 지정해야 합니다.");
                return this;
            }

            _clipIndex = index;
            _clip = _engine.GetTrack(_cachedTag, index);

            SetRandomClip(false);

            return this;
        }

        /// <summary>사운드 발생 위치를 지정한다.</summary>
        public Music SetPosition(Vector3 position)
        {
            _position = position;
            return this;
        }

        /// <summary>매 프레임 위치를 따라갈 대상을 지정한다.</summary>
        public Music SetFollowTarget(Transform followTarget)
        {
            _followTarget = followTarget;
            return this;
        }

        /// <summary>true면 3D, false면 2D(전역) 사운드.</summary>
        public Music SetSpatialSound(bool activate = true)
        {
            _spatialSound = activate;
            return this;
        }

        /// <summary>3D 오클루전을 켠다. 켜면 레이캐스트를 위해 자동으로 3D 모드가 된다.</summary>
        public Music SetOcclusion(bool activate = true)
        {
            _useOcclusion = activate;

            if (activate)
            {
                _spatialSound = true;
            }

            return this;
        }

        /// <summary>재생이 끝날 때 적용할 페이드 아웃 시간(초).</summary>
        public Music SetFadeOut(float fadeOutTime)
        {
            _fadeOutTime = fadeOutTime;
            return this;
        }

        /// <summary>AudioMixer Output을 지정해 볼륨 그룹을 관리한다.</summary>
        public Music SetOutput(Output output)
        {
            _output = output.IsNull ? null : _engine.GetOutput(output);
            return this;
        }

        /// <summary>재생 시작 시 호출될 콜백.</summary>
        public Music OnPlay(Action onPlay)
        {
            _onPlay = onPlay;
            return this;
        }

        /// <summary>재생 완료 시 호출될 콜백. loop 중이면 수동 Stop 시 호출된다.</summary>
        public Music OnComplete(Action onComplete)
        {
            _onComplete = onComplete;
            return this;
        }

        /// <summary>루프 한 바퀴가 끝날 때 호출될 콜백. loop가 true여야 한다.</summary>
        public Music OnLoopCycleComplete(Action onLoopCycleComplete)
        {
            _onLoopCycleComplete = onLoopCycleComplete;
            return this;
        }

        /// <summary>일시정지 시 호출될 콜백(페이드 아웃 시간 무시).</summary>
        public Music OnPause(Action onPause)
        {
            _onPause = onPause;
            return this;
        }

        /// <summary>일시정지 페이드 아웃까지 끝났을 때 호출될 콜백.</summary>
        public Music OnPauseComplete(Action onPauseComplete)
        {
            _onPauseComplete = onPauseComplete;
            return this;
        }

        /// <summary>재개 시 호출될 콜백.</summary>
        public Music OnResume(Action onResume)
        {
            _onResume = onResume;
            return this;
        }

        /// <summary>재생한다.</summary>
        /// <param name="fadeInTime">페이드 인 시간(초)</param>
        public void Play(float fadeInTime = 0f)
        {
            if (_clip == null && string.IsNullOrEmpty(_cachedTag))
            {
                Debug.LogError("[SoundService] 재생하기 전에 클립을 지정해야 합니다.");
                return;
            }

            if (string.Equals(_cachedTag, Track.NULL_TAG)) return;

            if (Using && Playing)
            {
                Stop();
                _forgetSourceOnStop = true;
            }

            if (_randomClip || _clip == null)
            {
                SetClip(_cachedTag);
            }
            else if (_clipIndex != -1)
            {
                SetClipByIndex(_clipIndex);
            }

            _source = _engine.GetSource();
            _source
                .SetVolume(_volume)
                .SetHearDistance(_minHearDistance, _maxHearDistance)
                .SetVolumeRolloffCurve(_audioRolloffMode, _customVolumeCurve)
                .SetPitch(_pitch)
                .SetDopplerLevel(_dopplerLevel)
                .SetLoop(_loop)
                .SetClip(_clip)
                .SetPosition(_position)
                .SetFollowTarget(_followTarget)
                .SetSpatialSound(_spatialSound)
                .SetOcclusion(_useOcclusion)
                .SetFadeOut(_fadeOutTime)
                .SetId(_id)
                .SetOutput(_output)
                .OnPlay(_onPlay)
                .OnComplete(_onComplete)
                .OnLoopCycleComplete(_onLoopCycleComplete)
                .OnPause(_onPause)
                .OnPauseComplete(_onPauseComplete)
                .OnResume(_onResume)
                .Play(fadeInTime);
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

            if (_forgetSourceOnStop)
            {
                _source.Stop(fadeOutTime);
                _source = null;
                _forgetSourceOnStop = false;
                return;
            }

            _source.Stop(fadeOutTime, () => _source = null);
        }
    }
}
