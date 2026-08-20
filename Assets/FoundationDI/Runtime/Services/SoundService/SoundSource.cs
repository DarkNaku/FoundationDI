using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 풀링되는 실제 재생 유닛. <see cref="Sound"/>/<see cref="Music"/>/<see cref="Playlist"/>/
    /// <see cref="DynamicMusic"/>가 설정을 흘려 넣고 재생을 지시한다.
    /// 오클루전·플레이리스트 진행·페이드는 모두 여기서 처리한다.
    /// </summary>
    internal class SoundSource : MonoBehaviour
    {
        private enum SourceState
        {
            Playing,
            Paused,
            Pausing,
            FadingIn,
            ChangingTrack,
            Stopping,
            Stopped
        }

        private ISoundEngine _engine;
        private AudioSource _source;
        private Transform _followTarget;
        private float _volume = 1f;
        private float _fadeOutTime;
        private float _fadeInTime;
        private bool _loop;
        private bool _stopping;
        private bool _changingTrack;
        private bool _isPlaylist;
        private Queue<AudioClip> _playlist = new();
        private float _playingTimeForNextSong;
        private float _playlistDuration;
        private bool _occlusionEnabled;
        private float _occlusionVolumeMultiplier = 1f;
        private AudioLowPassFilter _lowPassFilter;
        private float _occlusionCheckTimer;
        private float _occlusionCurrentFactor;
        private float _occlusionTargetFactor;

        private Coroutine _lerpVolumeCor;
        private Coroutine _fadeInOnChangeTrackCor;
        private Coroutine _lerpPitchCor;

        private SourceState _currentState = SourceState.Stopped;

        private Action _onPlay;
        private Action _onComplete;
        private Action _onLoopCycleComplete;
        private Action _onNextTrackStart;
        private Action _onPause;
        private Action _onPauseComplete;
        private Action _onResume;

        internal bool Using { get; private set; }
        internal bool Playing => _source != null && _source.isPlaying;
        internal float Volume => _source != null ? _source.volume : 0f;
        internal float Pitch => _source != null ? _source.pitch : 1f;
        internal bool Paused { get; private set; }
        internal string Id { get; private set; }
        internal float PlayingTime { get; private set; }
        internal float CurrentLoopCycleTime => _source != null ? _source.time : 0f;
        internal int CompletedLoopCycles { get; private set; }
        internal int ReproducedTracks { get; private set; }
        internal float CurrentClipDuration => _source != null && _source.clip != null ? _source.clip.length : 0f;
        internal AudioClip CurrentClip => _source != null ? _source.clip : null;
        internal AudioClip NextPlaylistClip => _playlist.Count > 0 ? _playlist.Peek() : null;

        private SoundServiceSettings Settings => _engine.Settings;

        private void Update()
        {
            if (!Using) return;

            if (!_loop)
            {
                if (!_isPlaylist)
                {
                    HandleSoundStop();
                }
                else
                {
                    HandlePlaylistStop();
                }
            }

            if (!_isPlaylist)
            {
                HandleSoundPlaying();
            }
            else
            {
                HandlePlaylistPlaying();
            }

            if (_occlusionEnabled)
            {
                UpdateOcclusion();
            }

            if (_followTarget == null) return;

            transform.position = _followTarget.position;
        }

        internal SoundSource Init(ISoundEngine engine, AudioSource source)
        {
            _engine = engine;
            _source = source;
            return this;
        }

        internal SoundSource MarkAsPlaylist()
        {
            _isPlaylist = true;
            return this;
        }

        internal SoundSource SetVolume(float volume, float lerpTime = 0f)
        {
            _volume = volume;

            if (_currentState is SourceState.Paused or SourceState.Stopping or SourceState.ChangingTrack)
            {
                Debug.LogWarning("[SoundService] 페이드 아웃이 진행 중이라 볼륨을 즉시 바꾸지 않습니다. " +
                                 $"다음 페이드 인에서 {volume}까지 올라갑니다.");
                return this;
            }

            if (lerpTime <= 0f)
            {
                _source.volume = volume * _occlusionVolumeMultiplier;
            }
            else
            {
                _lerpVolumeCor = StartCoroutine(LerpVolume(volume, lerpTime));
            }

            return this;
        }

        internal SoundSource SetHearDistance(float minHearDistance, float maxHearDistance)
        {
            _source.minDistance = minHearDistance;
            _source.maxDistance = maxHearDistance;
            return this;
        }

        internal SoundSource SetVolumeRolloffCurve(AudioRolloffMode audioRolloffMode, AnimationCurve customVolumeCurve)
        {
            _source.rolloffMode = audioRolloffMode;

            if (audioRolloffMode == AudioRolloffMode.Custom && customVolumeCurve != null)
            {
                _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customVolumeCurve);
            }

            return this;
        }

        internal SoundSource SetPitch(float pitch, float lerpTime = 0f)
        {
            if (lerpTime <= 0f)
            {
                _source.pitch = pitch;
            }
            else
            {
                if (_lerpPitchCor != null)
                {
                    StopCoroutine(_lerpPitchCor);
                    _lerpPitchCor = null;
                }

                _lerpPitchCor = StartCoroutine(LerpPitch(pitch, lerpTime));
            }

            return this;
        }

        internal SoundSource SetDopplerLevel(float dopplerLevel)
        {
            _source.dopplerLevel = dopplerLevel;
            return this;
        }

        internal SoundSource SetClip(AudioClip audioClip)
        {
            _source.clip = audioClip;
            return this;
        }

        internal SoundSource SetPlaylist(Queue<AudioClip> playlist)
        {
            _playlist = new Queue<AudioClip>(playlist);
            _playlistDuration = 0f;

            foreach (var clip in playlist)
            {
                _playlistDuration += clip != null ? clip.length : 0f;
            }

            return this;
        }

        internal void AddToPlaylist(AudioClip addedClip)
        {
            if (addedClip == null) return;

            _playlistDuration += addedClip.length;
            _playlist.Enqueue(addedClip);
        }

        internal SoundSource SetId(string id)
        {
            Id = id;
            return this;
        }

        internal SoundSource SetSpatialSound(bool activate)
        {
            _source.spatialBlend = activate ? 1f : 0f;
            return this;
        }

        internal SoundSource SetOcclusion(bool enable)
        {
            if (!Settings.EnableOcclusion) return this;

            _occlusionEnabled = enable;

            if (!enable)
            {
                _occlusionVolumeMultiplier = 1f;
                _occlusionCheckTimer = 0f;
                _occlusionCurrentFactor = 0f;
                _occlusionTargetFactor = 0f;

                if (_lowPassFilter != null)
                {
                    _lowPassFilter.enabled = false;
                    _lowPassFilter.cutoffFrequency = Settings.MaxCutoff;
                }

                if (_source != null)
                {
                    _source.volume = _volume;
                }

                return this;
            }

            if (_lowPassFilter == null)
            {
                _lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
            }

            _lowPassFilter.enabled = true;
            _lowPassFilter.cutoffFrequency = Settings.MaxCutoff;

            _occlusionCheckTimer = 0f;
            _occlusionTargetFactor = 0f;
            _occlusionCurrentFactor = 0f;
            _occlusionVolumeMultiplier = 1f;

            return this;
        }

        internal SoundSource SetPosition(Vector3 position)
        {
            transform.position = position;
            return this;
        }

        internal SoundSource SetFollowTarget(Transform followTarget)
        {
            _followTarget = followTarget;
            return this;
        }

        internal SoundSource SetFadeIn(float fadeInTime)
        {
            _fadeInTime = fadeInTime;
            return this;
        }

        internal SoundSource SetFadeOut(float fadeOutTime)
        {
            _fadeOutTime = fadeOutTime;
            return this;
        }

        internal SoundSource SetLoop(bool loop)
        {
            _loop = loop;
            _source.loop = loop;
            return this;
        }

        internal SoundSource SetOutput(AudioMixerGroup output)
        {
            _source.outputAudioMixerGroup = output;
            return this;
        }

        internal SoundSource OnPlay(Action onPlay)
        {
            _onPlay = onPlay;
            return this;
        }

        internal SoundSource OnComplete(Action onComplete)
        {
            _onComplete = onComplete;
            return this;
        }

        internal SoundSource OnLoopCycleComplete(Action onLoopCycleComplete)
        {
            _onLoopCycleComplete = onLoopCycleComplete;
            return this;
        }

        internal SoundSource OnNextTrackStart(Action onNextTrackStart)
        {
            _onNextTrackStart = onNextTrackStart;
            return this;
        }

        internal SoundSource OnPause(Action onPause)
        {
            _onPause = onPause;
            return this;
        }

        internal SoundSource OnPauseComplete(Action onPauseComplete)
        {
            _onPauseComplete = onPauseComplete;
            return this;
        }

        internal SoundSource OnResume(Action onResume)
        {
            _onResume = onResume;
            return this;
        }

        internal void Play(float fadeInTime = 0f)
        {
            if (_source.clip == null)
            {
                Debug.LogError("[SoundService] 오디오 클립이 없습니다. 선언부가 아니라 Awake/Start에서 초기화했는지 확인하세요.");
                return;
            }

            Using = true;
            Paused = false;
            PlayingTime = 0f;
            CompletedLoopCycles = 0;

            _onPlay?.Invoke();

            _source.Play();
            ChangeState(SourceState.Playing);
            enabled = true;

            if (fadeInTime <= 0f) return;

            ChangeState(SourceState.FadingIn);
            _source.volume = 0f;
            _lerpVolumeCor = StartCoroutine(LerpVolume(_volume, fadeInTime, () => ChangeState(SourceState.Playing)));
        }

        internal void PlayPlaylist(float fadeInTime)
        {
            foreach (var clip in _playlist)
            {
                if (clip != null) continue;

                Debug.LogError("[SoundService] 플레이리스트에 유효하지 않은 오디오 클립이 있습니다.");
                return;
            }

            Using = true;
            Paused = false;
            PlayingTime = 0f;
            ReproducedTracks = 0;
            CompletedLoopCycles = 0;
            _playingTimeForNextSong = 0f;

            if (_loop)
            {
                _source.loop = false;
            }

            _changingTrack = false;

            // 첫 트랙도 ReproducedTracks/onNextTrackStart에 반영된다.
            PlayNextSong();
            enabled = true;

            if (fadeInTime <= 0f) return;

            ChangeState(SourceState.FadingIn);
            _source.volume = 0f;
            _lerpVolumeCor = StartCoroutine(LerpVolume(_volume, fadeInTime, () => ChangeState(SourceState.Playing)));
        }

        internal void Pause(float fadeOutTime = 0f)
        {
            if (!Using) return;
            if (Paused) return;

            Paused = true;

            _onPause?.Invoke();

            void CompletePause()
            {
                _onPauseComplete?.Invoke();
                _source.Pause();
                ChangeState(SourceState.Paused);
            }

            if (_changingTrack)
            {
                CompletePause();
                return;
            }

            if (fadeOutTime > 0f)
            {
                if (_currentState == SourceState.FadingIn && _fadeInOnChangeTrackCor != null)
                {
                    StopCoroutine(_fadeInOnChangeTrackCor);
                    _fadeInOnChangeTrackCor = null;
                }

                StopLerpCoroutine();
                ChangeState(SourceState.Pausing);
                _lerpVolumeCor = StartCoroutine(LerpVolume(0f, fadeOutTime, CompletePause, true));
                return;
            }

            CompletePause();
        }

        internal void Resume(float fadeInTime = 0f)
        {
            if (!Paused) return;

            _onResume?.Invoke();

            Paused = false;
            _source.UnPause();
            ChangeState(SourceState.Playing);

            if (_changingTrack) return;
            if (fadeInTime <= 0f) return;

            StopLerpCoroutine();
            ChangeState(SourceState.FadingIn);
            _lerpVolumeCor = StartCoroutine(LerpVolume(_volume, fadeInTime, () => ChangeState(SourceState.Playing)));
        }

        internal void Stop(float fadeOutTime = 0f, Action onStop = null)
        {
            if (fadeOutTime > 0f)
            {
                _stopping = true;
                ChangeState(SourceState.Stopping);
                _lerpVolumeCor = StartCoroutine(LerpVolume(0f, fadeOutTime, () => Stop(0f, onStop)));
                return;
            }

            _onComplete?.Invoke();
            onStop?.Invoke();

            _source.Stop();
            ChangeState(SourceState.Stopped);
            _source.clip = null;

            _playlist.Clear();
            _isPlaylist = false;

            _followTarget = null;

            _onPlay = null;
            _onComplete = null;
            _onLoopCycleComplete = null;
            _onNextTrackStart = null;
            _onPause = null;
            _onPauseComplete = null;
            _onResume = null;

            Id = null;

            _occlusionEnabled = false;
            _occlusionVolumeMultiplier = 1f;
            _occlusionCheckTimer = 0f;

            if (_lowPassFilter != null)
            {
                _lowPassFilter.enabled = false;
                _lowPassFilter.cutoffFrequency = Settings.MaxCutoff;
            }

            _changingTrack = false;
            _stopping = false;
            Paused = false;
            Using = false;
            enabled = false;
        }

        private void UpdateOcclusion()
        {
            _occlusionCheckTimer -= Time.deltaTime;

            if (_occlusionCheckTimer <= 0f)
            {
                _occlusionCheckTimer = Settings.CheckInterval;

                if (_engine.TryGetListener(out var listener))
                {
                    _engine.CalculateOcclusion(listener.transform.position, transform.position, out float factor);
                    _occlusionTargetFactor = factor;
                }
            }

            float lerpSpeed = Settings.LerpSpeed;

            _occlusionCurrentFactor =
                Mathf.Lerp(_occlusionCurrentFactor, _occlusionTargetFactor, Time.deltaTime * lerpSpeed);

            float cutoff = Mathf.Lerp(Settings.MaxCutoff, Settings.MinCutoff, _occlusionCurrentFactor);
            float volumeMultiplier = Mathf.Lerp(1f, Settings.MinVolumeMultiplier, _occlusionCurrentFactor);

            _occlusionVolumeMultiplier = volumeMultiplier;

            if (_lowPassFilter != null)
            {
                _lowPassFilter.cutoffFrequency = cutoff;
            }

            if (_source != null)
            {
                _source.volume = _volume * _occlusionVolumeMultiplier;
            }
        }

        private void HandleSoundPlaying()
        {
            PlayingTime += Time.deltaTime;

            if (!_loop) return;
            if (PlayingTime > CurrentClipDuration * CompletedLoopCycles + 1f) return;

            CompletedLoopCycles++;

            _onLoopCycleComplete?.Invoke();
        }

        private void HandlePlaylistPlaying()
        {
            PlayingTime += Time.deltaTime;

            if (_loop && CurrentLoopCycleTime >= _playlistDuration - _fadeOutTime)
            {
                CompletedLoopCycles++;
                _onLoopCycleComplete?.Invoke();
            }

            if (PlayingTime < _playingTimeForNextSong - _fadeOutTime || _changingTrack) return;

            _changingTrack = true;
            ChangeState(SourceState.ChangingTrack);

            if (_fadeOutTime > 0f)
            {
                _lerpVolumeCor = StartCoroutine(LerpVolume(0f, _fadeOutTime, () => PlayNextSong()));
            }
            else
            {
                PlayNextSong();
            }
        }

        private void HandleSoundStop()
        {
            if (_stopping) return;

            if (_fadeOutTime > 0f && PlayingTime >= CurrentClipDuration - 0.05f - _fadeOutTime)
            {
                Stop(_fadeOutTime);
                return;
            }

            if (!Playing && !Paused)
            {
                Stop();
            }
        }

        private void HandlePlaylistStop()
        {
            if (_playlist.Count > 0) return;
            if (_stopping) return;

            if (_fadeOutTime > 0f && CurrentLoopCycleTime >= CurrentClipDuration - 0.05f - _fadeOutTime)
            {
                Stop(_fadeOutTime);
                return;
            }

            if (!Playing && !Paused)
            {
                Stop();
            }
        }

        private void PlayNextSong(bool firstTrack = false)
        {
            if (_playlist.Count == 0) return;

            _source.clip = _playlist.Dequeue();

            if (_loop)
            {
                _playlist.Enqueue(_source.clip);
            }

            PlayingTime = _playingTimeForNextSong;
            _playingTimeForNextSong += CurrentClipDuration;

            if (!firstTrack)
            {
                ReproducedTracks++;
                _onNextTrackStart?.Invoke();
            }

            _changingTrack = false;

            if (Paused) return;

            _source.Play();

            if (_fadeInTime > 0f)
            {
                ChangeState(SourceState.FadingIn);
                _fadeInOnChangeTrackCor = StartCoroutine(LerpVolume(_volume, _fadeInTime,
                    () => ChangeState(SourceState.Playing)));
            }
            else
            {
                ChangeState(SourceState.Playing);
                _source.volume = _volume * _occlusionVolumeMultiplier;
            }
        }

        private void StopLerpCoroutine()
        {
            if (_lerpVolumeCor == null) return;

            StopCoroutine(_lerpVolumeCor);
            _lerpVolumeCor = null;
        }

        private void ChangeState(SourceState newState) => _currentState = newState;

        private IEnumerator LerpVolume(float newVolume, float lerpTime, Action onFinishLerp = null,
            bool ignorePause = false)
        {
            float initialBaseVolume = _volume;
            float targetBaseVolume = newVolume;

            for (float t = 0f; t < lerpTime; t += Time.deltaTime)
            {
                if (!ignorePause)
                {
                    while (Paused)
                    {
                        yield return null;
                    }
                }

                float lerpedBase = Mathf.Lerp(initialBaseVolume, targetBaseVolume, t / lerpTime);
                _volume = lerpedBase;
                _source.volume = lerpedBase * _occlusionVolumeMultiplier;

                yield return null;
            }

            _volume = targetBaseVolume;
            _source.volume = _volume * _occlusionVolumeMultiplier;

            onFinishLerp?.Invoke();

            _lerpVolumeCor = null;
        }

        private IEnumerator LerpPitch(float newPitch, float lerpTime)
        {
            float initialPitch = _source.pitch;

            for (float t = 0f; t < lerpTime; t += Time.deltaTime)
            {
                while (Paused)
                {
                    yield return null;
                }

                _source.pitch = Mathf.Lerp(initialPitch, newPitch, t / lerpTime);

                yield return null;
            }

            _source.pitch = newPitch;
            _lerpPitchCor = null;
        }
    }
}
