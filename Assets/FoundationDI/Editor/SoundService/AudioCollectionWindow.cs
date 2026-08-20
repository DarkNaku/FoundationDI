using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// 이미 만들어진 사운드/음악을 검색하고 태그·클립을 수정하거나 삭제하는 창.
    /// </summary>
    public class AudioCollectionWindow : EditorWindow
    {
        private sealed class EditState
        {
            public string Tag;
            public List<AudioClip> Clips;
        }

        private Sections _currentSection = Sections.Sounds;
        private string _searchTag = string.Empty;
        private AudioClip _searchClip;
        private Vector2 _scroll;

        private readonly Dictionary<string, EditState> _editStates = new();

        private string _pendingRemoveTag;

        private bool IsSoundsSection => _currentSection == Sections.Sounds;

        [MenuItem("Tools/DarkNaku/FoundationDI/Sound/Audio Collection", false, 51)]
        public static void ShowWindow()
        {
            var window = GetWindow<AudioCollectionWindow>();
            window.titleContent = new GUIContent("Audio Collection");
            window.minSize = new Vector2(420f, 420f);
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawTabs();
                EditorGUILayout.Space(6f);
                DrawSearchFields();
                EditorGUILayout.Space(6f);

                var settings = SoundServiceAssetLocator.FindSettings();

                if (settings == null)
                {
                    EditorGUILayout.HelpBox("SoundServiceSettings 에셋이 없습니다. Audio Creator를 한 번 열면 생성됩니다.",
                        MessageType.Warning);
                    return;
                }

                SoundServiceAssetLocator.EnsureCollections(settings);

                var results = Search(settings);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                if (results.Length == 0)
                {
                    EditorGUILayout.HelpBox("결과가 없습니다.", MessageType.Info);
                }

                foreach (var soundData in results)
                {
                    DrawAudioEntry(settings, soundData);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawTabButton("Sounds", IsSoundsSection))
            {
                ChangeTab(Sections.Sounds);
            }

            if (DrawTabButton("Music", !IsSoundsSection))
            {
                ChangeTab(Sections.Music);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static bool DrawTabButton(string label, bool selected)
        {
            var previousColor = GUI.backgroundColor;

            GUI.backgroundColor = selected ? SoundEditorHelper.OrangeColor : SoundEditorHelper.GreyColor;

            bool clicked = GUILayout.Button(label, GUILayout.Height(26f));

            GUI.backgroundColor = previousColor;

            return clicked;
        }

        private void ChangeTab(Sections section)
        {
            if (_currentSection == section) return;

            _currentSection = section;
            _editStates.Clear();
            _pendingRemoveTag = null;
        }

        private void DrawSearchFields()
        {
            EditorGUILayout.BeginHorizontal();
            _searchTag = EditorGUILayout.TextField("Search Tag", _searchTag);

            if (GUILayout.Button("x", GUILayout.Width(24f)))
            {
                _searchTag = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _searchClip = (AudioClip)EditorGUILayout.ObjectField("Search Clip", _searchClip, typeof(AudioClip), false);

            if (GUILayout.Button("x", GUILayout.Width(24f)))
            {
                _searchClip = null;
            }

            EditorGUILayout.EndHorizontal();
        }

        private SoundData[] Search(SoundServiceSettings settings)
        {
            var all = IsSoundsSection
                ? settings.SoundDataCollection.Sounds
                : settings.MusicDataCollection.MusicTracks;

            bool searchByTag = !string.IsNullOrWhiteSpace(_searchTag);
            bool searchByClip = _searchClip != null;

            if (!searchByTag && !searchByClip) return all;

            return all.Where(soundData =>
            {
                bool tagMatches = !searchByTag ||
                                  soundData.Tag.StartsWith(_searchTag, System.StringComparison.OrdinalIgnoreCase);
                bool clipMatches = !searchByClip ||
                                   (soundData.Clips != null && soundData.Clips.Any(clip => clip == _searchClip));

                return tagMatches && clipMatches;
            }).ToArray();
        }

        private void DrawAudioEntry(SoundServiceSettings settings, SoundData soundData)
        {
            string originalTag = soundData.Tag;

            if (!_editStates.TryGetValue(originalTag, out var state))
            {
                state = new EditState
                {
                    Tag = originalTag,
                    Clips = soundData.Clips != null ? soundData.Clips.ToList() : new List<AudioClip>()
                };

                _editStates[originalTag] = state;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            state.Tag = EditorGUILayout.TextField("Tag", state.Tag);

            if (GUILayout.Button("Add Clip", GUILayout.Width(70f)))
            {
                state.Clips.Add(null);
            }

            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < state.Clips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                state.Clips[i] = (AudioClip)EditorGUILayout.ObjectField(state.Clips[i], typeof(AudioClip), false);

                if (GUILayout.Button("x", GUILayout.Width(24f)))
                {
                    state.Clips.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField(
                $"{soundData.CompressionPreset} / {(soundData.ForceToMono ? "Mono" : "Stereo")}",
                EditorStyles.miniLabel);

            DrawEntryActions(settings, soundData, originalTag, state);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        private void DrawEntryActions(SoundServiceSettings settings, SoundData soundData, string originalTag,
            EditState state)
        {
            bool tagChanged = state.Tag != originalTag;
            bool clipsChanged = soundData.Clips == null || !state.Clips.SequenceEqual(soundData.Clips);
            bool hasChanges = tagChanged || clipsChanged;

            if (hasChanges)
            {
                string warning = "변경 사항을 적용할까요?";

                if (tagChanged)
                {
                    warning += "\n태그를 바꾸면 코드에서 이 오디오를 참조하던 부분이 끊어질 수 있습니다.";
                }

                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();

            bool canApply = hasChanges && SoundEditorHelper.IsTagValid(state.Tag) && state.Clips.All(c => c != null);

            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button("Apply"))
                {
                    ApplyChanges(settings, originalTag, state);
                }
            }

            using (new EditorGUI.DisabledScope(!hasChanges))
            {
                if (GUILayout.Button("Undo"))
                {
                    _editStates.Remove(originalTag);
                }
            }

            if (_pendingRemoveTag == originalTag)
            {
                var previousColor = GUI.backgroundColor;

                GUI.backgroundColor = SoundEditorHelper.RedColor;

                if (GUILayout.Button("정말 삭제"))
                {
                    Remove(settings, originalTag);
                }

                GUI.backgroundColor = previousColor;

                if (GUILayout.Button("취소"))
                {
                    _pendingRemoveTag = null;
                }
            }
            else if (GUILayout.Button("Remove"))
            {
                _pendingRemoveTag = originalTag;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyChanges(SoundServiceSettings settings, string originalTag, EditState state)
        {
            string resultMessage;
            bool success;

            if (IsSoundsSection)
            {
                success = settings.SoundDataCollection.EditSound(originalTag, state.Tag, state.Clips.ToArray(),
                    out resultMessage);
            }
            else
            {
                success = settings.MusicDataCollection.EditMusic(originalTag, state.Tag, state.Clips.ToArray(),
                    out resultMessage);
            }

            if (!success)
            {
                Debug.LogError($"[SoundService] {resultMessage}");
                return;
            }

            var soundData = IsSoundsSection
                ? settings.SoundDataCollection.GetSound(state.Tag)
                : settings.MusicDataCollection.GetMusicTrack(state.Tag);

            if (soundData != null)
            {
                SoundEditorHelper.ChangeAudioClipImportSettings(soundData.Clips, soundData.CompressionPreset,
                    soundData.ForceToMono);
            }

            SoundEditorHelper.SaveCollectionChanges(_currentSection);

            _editStates.Clear();

            Debug.Log($"[SoundService] {resultMessage}");
        }

        private void Remove(SoundServiceSettings settings, string tag)
        {
            if (IsSoundsSection)
            {
                settings.SoundDataCollection.RemoveSound(tag);
            }
            else
            {
                settings.MusicDataCollection.RemoveMusicTrack(tag);
            }

            SoundEditorHelper.SaveCollectionChanges(_currentSection);

            _editStates.Clear();
            _pendingRemoveTag = null;
        }
    }
}
