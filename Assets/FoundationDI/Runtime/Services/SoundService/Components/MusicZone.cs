using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 구/박스 영역 안에서만 들리는 음악 존. 영역 밖 페이드 구간에서 거리에 비례해 볼륨이 줄어든다.
    /// 씬에 배치하는 컴포넌트이므로 <see cref="InjectableBehaviour"/>로 <see cref="ISoundService"/>를 주입받는다.
    /// </summary>
    public class MusicZone : InjectableBehaviour
    {
        public enum Shape
        {
            Sphere,
            Box
        }

        public enum PlayerMode
        {
            Music,
            Playlist,
            DynamicMusic
        }

        [Serializable]
        public class TrackInfo
        {
            public Track track = default;
            public float volume = 1f;
        }

        [Inject] private ISoundService _soundService;

        [Header("Shape")]
        public Shape zoneShape;

        public bool useScaleAsZoneSize;
        public bool drawWireframe = true;

        public float radius = 1f;
        public float extraRadiusFade = 1f;
        public float height = 1f;
        public float width = 1f;
        public float depth = 1f;
        public float extraBoxSizeFade = 1f;

        public Color areaColor = new(0.992f, 0.694f, 0.012f);
        public Color fadeColor = new(1f, 0.953f, 0.847f);

        [Header("Music")]
        public PlayerMode playerMode = PlayerMode.Music;

        public List<Track> tracks = new();
        public float volume = 1f;

        public List<TrackInfo> dynamicTracks = new();

        public bool loop;
        public Output output = default;

        private Transform _playerCamera;

        private Music _music;
        private Playlist _playlist;
        private DynamicMusic _dynamicMusic;

        private Vector3 _closestPoint;
        private Vector3 _closestFadePoint;
        private float _maxDistanceBoxFade;

        private Vector3 _boxSize;
        private Vector3 _fadeBoxSize;
        private float _fadeRadius;

        private bool _playerJustExitFadeZone;
        private bool _playerJustEnterMusicZone;

        protected override void Awake()
        {
            base.Awake();

            var mainCamera = Camera.main;

            if (mainCamera != null)
            {
                _playerCamera = mainCamera.transform;
            }

            RefreshZoneSize();
        }

        private void Start()
        {
            EnsureInjected();

            if (_soundService == null)
            {
                Debug.LogError("[MusicZone] ISoundService가 주입되지 않았습니다.");
                enabled = false;
                return;
            }

            switch (playerMode)
            {
                case PlayerMode.Music:
                    StartMusic();
                    break;

                case PlayerMode.Playlist:
                    StartPlaylist();
                    break;

                default:
                    StartDynamicMusic();
                    break;
            }

            _playerJustEnterMusicZone = true;

            MuteVolume();
        }

        private void Update()
        {
            if (_playerCamera == null) return;

            if (zoneShape == Shape.Box)
            {
                HandleBoxVolume();
                return;
            }

            HandleSphereVolume();
        }

        private void OnDrawGizmos()
        {
            RefreshZoneSize();

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_closestPoint, 0.3f);
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(_closestFadePoint, 0.3f);

            if (zoneShape == Shape.Box)
            {
                Gizmos.color = areaColor;

                if (drawWireframe)
                {
                    Gizmos.DrawWireCube(transform.position, _boxSize);
                }
                else
                {
                    Gizmos.DrawCube(transform.position, _boxSize);
                }

                if (extraBoxSizeFade <= 0f) return;

                Gizmos.color = fadeColor;

                if (drawWireframe)
                {
                    Gizmos.DrawWireCube(transform.position, _fadeBoxSize);
                }
                else
                {
                    Gizmos.DrawCube(transform.position, _fadeBoxSize);
                }

                return;
            }

            Gizmos.color = areaColor;

            if (drawWireframe)
            {
                Gizmos.DrawWireSphere(transform.position, radius);
            }
            else
            {
                Gizmos.DrawSphere(transform.position, radius);
            }

            if (extraRadiusFade <= 0f) return;

            Gizmos.color = fadeColor;

            if (drawWireframe)
            {
                Gizmos.DrawWireSphere(transform.position, _fadeRadius);
            }
            else
            {
                Gizmos.DrawSphere(transform.position, _fadeRadius);
            }
        }

        private void RefreshZoneSize()
        {
            _boxSize = useScaleAsZoneSize ? transform.localScale : new Vector3(width, height, depth);
            _fadeBoxSize = _boxSize * (extraBoxSizeFade + 1f);
            radius = useScaleAsZoneSize ? transform.localScale.x : radius;
            _fadeRadius = radius * (extraRadiusFade + 1f);
        }

        private void StartMusic()
        {
            if (tracks.Count == 0)
            {
                Debug.LogError("[MusicZone] 재생할 트랙이 지정되지 않았습니다.");
                enabled = false;
                return;
            }

            _music = _soundService.CreateMusic(tracks[0]);
            _music
                .SetLoop(loop)
                .SetVolume(volume)
                .SetSpatialSound(false)
                .SetPosition(transform.position)
                .SetOutput(output)
                .Play();
        }

        private void StartPlaylist()
        {
            if (tracks.Count == 0)
            {
                Debug.LogError("[MusicZone] 재생할 트랙이 지정되지 않았습니다.");
                enabled = false;
                return;
            }

            _playlist = _soundService.CreatePlaylist(tracks.ToArray());
            _playlist
                .SetLoop(loop)
                .SetVolume(volume)
                .SetSpatialSound(false)
                .SetPosition(transform.position)
                .SetOutput(output)
                .Play();
        }

        private void StartDynamicMusic()
        {
            if (dynamicTracks.Count == 0)
            {
                Debug.LogError("[MusicZone] 재생할 다이내믹 트랙이 지정되지 않았습니다.");
                enabled = false;
                return;
            }

            var dynamicTrackKeys = new Track[dynamicTracks.Count];

            for (int i = 0; i < dynamicTracks.Count; i++)
            {
                dynamicTrackKeys[i] = dynamicTracks[i].track;
            }

            _dynamicMusic = _soundService.CreateDynamicMusic(dynamicTrackKeys);
            _dynamicMusic
                .SetLoop(loop)
                .SetSpatialSound(false)
                .SetPosition(transform.position)
                .SetOutput(output);

            foreach (var dynamicTrack in dynamicTracks)
            {
                _dynamicMusic.SetTrackVolume(dynamicTrack.track, dynamicTrack.volume);
            }

            _dynamicMusic.Play();
        }

        private void HandleBoxVolume()
        {
            var cameraPosition = _playerCamera.position;
            var boxCenter = transform.position;

            var halfSize = _boxSize / 2f;
            var boxMin = boxCenter - halfSize;
            var boxMax = boxCenter + halfSize;

            var fadeHalfSize = _fadeBoxSize / 2f;
            var fadeBoxMin = boxCenter - fadeHalfSize;
            var fadeBoxMax = boxCenter + fadeHalfSize;

            bool playerInsideBox = cameraPosition.x >= boxMin.x && cameraPosition.x <= boxMax.x &&
                                   cameraPosition.y >= boxMin.y && cameraPosition.y <= boxMax.y &&
                                   cameraPosition.z >= boxMin.z && cameraPosition.z <= boxMax.z;

            bool playerInsideFade = cameraPosition.x >= fadeBoxMin.x && cameraPosition.x <= fadeBoxMax.x &&
                                    cameraPosition.y >= fadeBoxMin.y && cameraPosition.y <= fadeBoxMax.y &&
                                    cameraPosition.z >= fadeBoxMin.z && cameraPosition.z <= fadeBoxMax.z;

            _closestPoint = new Vector3(
                Mathf.Clamp(cameraPosition.x, boxMin.x, boxMax.x),
                Mathf.Clamp(cameraPosition.y, boxMin.y, boxMax.y),
                Mathf.Clamp(cameraPosition.z, boxMin.z, boxMax.z));

            _closestFadePoint = new Vector3(
                Mathf.Clamp(cameraPosition.x, fadeBoxMin.x, fadeBoxMax.x),
                Mathf.Clamp(cameraPosition.y, fadeBoxMin.y, fadeBoxMax.y),
                Mathf.Clamp(cameraPosition.z, fadeBoxMin.z, fadeBoxMax.z));

            if (playerInsideBox)
            {
                if (_playerJustEnterMusicZone)
                {
                    SetMaxVolume();
                    _playerJustEnterMusicZone = false;
                }

                return;
            }

            if (!_playerJustEnterMusicZone)
            {
                _playerJustEnterMusicZone = true;
            }

            if (!playerInsideFade)
            {
                if (_playerJustExitFadeZone)
                {
                    MuteVolume();
                    _playerJustExitFadeZone = false;
                }

                _maxDistanceBoxFade = Vector3.Distance(_closestPoint, _closestFadePoint);
                return;
            }

            if (Mathf.Approximately(_maxDistanceBoxFade, 0f))
            {
                var directionToCamera = (_closestPoint - _playerCamera.position).normalized;
                var relativeCameraPosition = _closestPoint + directionToCamera * (extraRadiusFade * 5f);
                _maxDistanceBoxFade = Vector3.Distance(relativeCameraPosition, _playerCamera.position);
            }

            ChangeVolumeInBoxFadeZone();

            if (!_playerJustExitFadeZone)
            {
                _playerJustExitFadeZone = true;
            }
        }

        private void HandleSphereVolume()
        {
            var directionToCamera = (_playerCamera.position - transform.position).normalized;

            _closestFadePoint = transform.position + directionToCamera * _fadeRadius;
            _closestPoint = transform.position + directionToCamera * radius;

            float distanceToSphere = Vector3.Distance(_closestPoint, _playerCamera.position);
            float distanceToCenter = Vector3.Distance(transform.position, _playerCamera.position);

            bool playerInsideFade = distanceToCenter < _fadeRadius;
            bool playerInsideSphere = distanceToCenter < radius;

            if (playerInsideSphere)
            {
                if (_playerJustEnterMusicZone)
                {
                    SetMaxVolume();
                    _playerJustEnterMusicZone = false;
                }

                return;
            }

            if (!playerInsideFade && _playerJustExitFadeZone)
            {
                MuteVolume();
                _playerJustEnterMusicZone = false;
            }

            ChangeVolumeInSphereFadeZone(distanceToSphere);

            if (!_playerJustEnterMusicZone)
            {
                _playerJustEnterMusicZone = true;
            }
        }

        private void ChangeVolumeInBoxFadeZone()
        {
            if (Mathf.Approximately(_maxDistanceBoxFade, 0f)) return;

            float currentDistance = Vector3.Distance(_closestPoint, _closestFadePoint);

            if (playerMode == PlayerMode.DynamicMusic)
            {
                foreach (var dynamicTrack in dynamicTracks)
                {
                    float trackVolume = dynamicTrack.volume -
                                        currentDistance * dynamicTrack.volume / _maxDistanceBoxFade;
                    _dynamicMusic.ChangeTrackVolume(dynamicTrack.track, trackVolume);
                }

                return;
            }

            float targetVolume = volume - currentDistance * volume / _maxDistanceBoxFade;

            if (playerMode == PlayerMode.Music)
            {
                _music.ChangeVolume(targetVolume);
            }
            else
            {
                _playlist.ChangeVolume(targetVolume);
            }
        }

        private void ChangeVolumeInSphereFadeZone(float distanceToSphere)
        {
            float maxDistance = Vector3.Distance(_closestFadePoint, _closestPoint);

            if (Mathf.Approximately(maxDistance, 0f)) return;

            if (playerMode == PlayerMode.DynamicMusic)
            {
                foreach (var dynamicTrack in dynamicTracks)
                {
                    float trackVolume = dynamicTrack.volume - distanceToSphere * dynamicTrack.volume / maxDistance;
                    _dynamicMusic.ChangeTrackVolume(dynamicTrack.track, trackVolume);
                }

                return;
            }

            float targetVolume = volume - distanceToSphere * volume / maxDistance;

            if (playerMode == PlayerMode.Music)
            {
                _music.ChangeVolume(targetVolume);
            }
            else
            {
                _playlist.ChangeVolume(targetVolume);
            }
        }

        private void MuteVolume()
        {
            float fadeOutTime = 0f;

            if (zoneShape == Shape.Box)
            {
                float currentDistance = Vector3.Distance(_closestPoint, _closestFadePoint);

                if (currentDistance < _maxDistanceBoxFade - 0.1f)
                {
                    fadeOutTime = 0.5f;
                }
            }

            if (playerMode == PlayerMode.DynamicMusic)
            {
                foreach (var dynamicTrack in dynamicTracks)
                {
                    _dynamicMusic.ChangeTrackVolume(dynamicTrack.track, 0f, fadeOutTime);
                }

                return;
            }

            if (playerMode == PlayerMode.Music)
            {
                _music.ChangeVolume(0f, fadeOutTime);
            }
            else
            {
                _playlist.ChangeVolume(0f, fadeOutTime);
            }
        }

        private void SetMaxVolume()
        {
            if (playerMode == PlayerMode.DynamicMusic)
            {
                foreach (var dynamicTrack in dynamicTracks)
                {
                    _dynamicMusic.ChangeTrackVolume(dynamicTrack.track, dynamicTrack.volume);
                }

                return;
            }

            if (playerMode == PlayerMode.Music)
            {
                _music.ChangeVolume(volume);
            }
            else
            {
                _playlist.ChangeVolume(volume);
            }
        }
    }
}
