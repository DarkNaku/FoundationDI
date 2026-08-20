using System.Linq;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI.Samples
{
    /// <summary>
    /// SoundService의 주요 기능을 한 화면에서 눌러 볼 수 있는 데모 패널.
    /// UI 프리팹 배선 대신 <see cref="OnGUI"/>로 그려서, 샘플이 사운드 API 자체에만 집중하도록 했다.
    /// </summary>
    public class SoundSampleDemo : InjectableBehaviour
    {
        [Inject] private ISoundService _sound;

        private Sound _click;
        private Sound _coin;
        private Music _music;
        private Playlist _playlist;
        private DynamicMusic _dynamicMusic;

        private float _musicVolume = 0.6f;
        private float _drumVolume = 1f;
        private float _bassVolume;
        private float _leadVolume;

        private bool _dataMissing;
        private Vector2 _scroll;

        private void Start()
        {
            EnsureInjected();

            if (_sound == null)
            {
                Debug.LogError("[SoundSampleDemo] ISoundService가 주입되지 않았습니다. SoundSampleScope를 확인하세요.");
                enabled = false;
                return;
            }

            _dataMissing = !IsSampleDataRegistered();

            if (_dataMissing) return;

            BuildSounds();
        }

        /// <summary>빌더는 한 번만 만들어 두고 재사용한다. Play()마다 풀에서 소스를 빌려 쓴다.</summary>
        private void BuildSounds()
        {
            _click = _sound.CreateSound(SoundSampleTags.Click)
                .SetVolume(0.7f)
                .SetSpatialSound(false);

            _coin = _sound.CreateSound(SoundSampleTags.Coin)
                .SetVolume(0.7f)
                .SetSpatialSound(false);

            _music = _sound.CreateMusic(SoundSampleTags.Song1)
                .SetLoop()
                .SetVolume(_musicVolume)
                .SetId("sample-music");

            _playlist = _sound.CreatePlaylist(SoundSampleTags.Song1, SoundSampleTags.Song2)
                .SetLoop()
                .SetVolume(0.6f)
                .SetFadeIn(0.4f)
                .SetFadeOut(0.4f)
                .SetId("sample-playlist")
                .OnNextTrackStart(() => Debug.Log("[SoundSampleDemo] 다음 트랙 시작"));

            _dynamicMusic = _sound.CreateDynamicMusic(
                    SoundSampleTags.LayerDrum, SoundSampleTags.LayerBass, SoundSampleTags.LayerLead)
                .SetLoop()
                .SetId("sample-dynamic");

            _dynamicMusic.SetTrackVolume(SoundSampleTags.LayerDrum, _drumVolume);
            _dynamicMusic.SetTrackVolume(SoundSampleTags.LayerBass, _bassVolume);
            _dynamicMusic.SetTrackVolume(SoundSampleTags.LayerLead, _leadVolume);
        }

        private bool IsSampleDataRegistered()
        {
            var settings = _sound.Settings;

            if (settings == null || settings.SoundDataCollection == null || settings.MusicDataCollection == null)
            {
                return false;
            }

            bool hasSfx = settings.SoundDataCollection.Sounds.Any(sound => sound.Tag == SoundSampleTags.Click);
            bool hasMusic = settings.MusicDataCollection.MusicTracks.Any(track => track.Tag == SoundSampleTags.Song1);

            return hasSfx && hasMusic;
        }

        private void OnGUI()
        {
            var area = new Rect(20f, 20f, 380f, Screen.height - 40f);

            GUILayout.BeginArea(area, GUI.skin.box);

            if (_dataMissing)
            {
                DrawMissingDataHelp();
                GUILayout.EndArea();
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawSfxSection();
            DrawMusicSection();
            DrawPlaylistSection();
            DrawDynamicMusicSection();
            DrawGlobalSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawMissingDataHelp()
        {
            GUILayout.Label("샘플 오디오 데이터가 등록되지 않았습니다.");
            GUILayout.Space(6f);
            GUILayout.Label("Tools > FoundationDI > Sound > Samples >\nImport Sample Audio 를 실행한 뒤 다시 Play 하세요.");
        }

        private void DrawSfxSection()
        {
            GUILayout.Label("── SFX ──");

            // 같은 태그에 클립 3개가 묶여 있어 누를 때마다 다른 클립이 나온다.
            if (GUILayout.Button("Click (랜덤 클립 + 랜덤 피치)"))
            {
                _click.SetRandomPitch().Play();
            }

            if (GUILayout.Button("Coin"))
            {
                _coin.Play();
            }

            if (GUILayout.Button("Coin (50% 확률로만 재생)"))
            {
                _coin.SetPlayProbability(0.5f).Play();
                _coin.SetPlayProbability(1f);
            }

            GUILayout.Space(8f);
        }

        private void DrawMusicSection()
        {
            GUILayout.Label($"── Music ── {(_music.Playing ? "재생 중" : _music.Paused ? "일시정지" : "정지")}");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Play (fade 1s)"))
            {
                _music.Play(1f);
            }

            if (GUILayout.Button("Stop (fade 1s)"))
            {
                _music.Stop(1f);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Pause (0.3s)"))
            {
                _music.Pause(0.3f);
            }

            if (GUILayout.Button("Resume (0.3s)"))
            {
                _music.Resume(0.3f);
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"Volume {_musicVolume:F2}");

            float newVolume = GUILayout.HorizontalSlider(_musicVolume, 0f, 1f);

            if (!Mathf.Approximately(newVolume, _musicVolume))
            {
                _musicVolume = newVolume;
                _music.ChangeVolume(newVolume, 0.1f);
            }

            GUILayout.Space(8f);
        }

        private void DrawPlaylistSection()
        {
            GUILayout.Label("── Playlist ──");
            GUILayout.Label($"순서: {string.Join(" → ", _playlist.PlaylistClipsTags)}");
            GUILayout.Label($"재생된 트랙 수: {_playlist.ReproducedTracks}");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Play"))
            {
                _playlist.Play();
            }

            if (GUILayout.Button("Shuffle"))
            {
                _playlist.Shuffle();
            }

            if (GUILayout.Button("Stop"))
            {
                _playlist.Stop(0.4f);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void DrawDynamicMusicSection()
        {
            GUILayout.Label("── Dynamic Music ──");
            GUILayout.Label("같은 길이의 레이어 3개를 동시에 재생하고 볼륨만 섞는다.");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Play"))
            {
                _dynamicMusic.Play(0.5f);
            }

            if (GUILayout.Button("Stop"))
            {
                _dynamicMusic.Stop(0.5f);
            }

            GUILayout.EndHorizontal();

            DrawLayerSlider("Drum", SoundSampleTags.LayerDrum, ref _drumVolume);
            DrawLayerSlider("Bass", SoundSampleTags.LayerBass, ref _bassVolume);
            DrawLayerSlider("Lead", SoundSampleTags.LayerLead, ref _leadVolume);

            GUILayout.Space(8f);
        }

        private void DrawLayerSlider(string label, string tag, ref float volume)
        {
            GUILayout.Label($"{label} {volume:F2}");

            float newVolume = GUILayout.HorizontalSlider(volume, 0f, 1f);

            if (Mathf.Approximately(newVolume, volume)) return;

            volume = newVolume;

            _dynamicMusic.ChangeTrackVolume(tag, newVolume, 0.2f);
        }

        private void DrawGlobalSection()
        {
            GUILayout.Label("── 전체 제어 ──");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("PauseAll"))
            {
                _sound.PauseAll(0.3f);
            }

            if (GUILayout.Button("ResumeAll"))
            {
                _sound.ResumeAll(0.3f);
            }

            if (GUILayout.Button("StopAll"))
            {
                _sound.StopAll(0.3f);
            }

            GUILayout.EndHorizontal();

            // id를 걸어 두면 빌더 참조 없이도 서비스에서 바로 제어할 수 있다.
            if (GUILayout.Button("id로 music만 정지 (\"sample-music\")"))
            {
                _sound.Stop("sample-music", 0.3f);
            }
        }
    }
}
