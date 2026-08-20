using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// 태그와 오디오 클립을 묶어 사운드/음악 데이터를 만드는 창.
    /// 같은 태그에 여러 클립을 넣으면 런타임에 무작위로 하나가 선택된다.
    /// </summary>
    public class AudioCreatorWindow : EditorWindow
    {
        private const int AudioClipPickerId = 8801;

        private const string FrequentSoundInfo = "짧고 가벼우며 자주 재생되는 소리(발사, 발소리, UI 등).";
        private const string OccasionalSoundInfo = "짧고 가볍지만 자주 재생되지는 않는 소리.";
        private const string AmbientMusicInfo = "길고 무거우며 오래 재생되는 음악.";

        private Sections _currentSection = Sections.Sounds;
        private readonly List<AudioClip> _importedClips = new();
        private string _currentTag = string.Empty;
        private CompressionPreset _currentCompressionPreset = CompressionPreset.FrequentSound;
        private bool _forceToMono;
        private bool _waitingForPickerResult;

        private string _resultMessage = string.Empty;
        private bool _resultIsError;

        private Vector2 _scroll;

        [MenuItem("Tools/FoundationDI/Sound/Audio Creator", false, 50)]
        public static void ShowWindow()
        {
            var window = GetWindow<AudioCreatorWindow>();
            window.titleContent = new GUIContent("Audio Creator");
            window.minSize = new Vector2(360f, 420f);
        }

        private void OnGUI()
        {
            HandleObjectPickerResult();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                DrawTabs();
                EditorGUILayout.Space(6f);
                DrawTagField();
                EditorGUILayout.Space(6f);
                DrawDropZone();
                DrawClipList();
                EditorGUILayout.Space(6f);
                DrawImportSettings();
                EditorGUILayout.Space(10f);
                DrawCreateButton();
                DrawResultMessage();

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawTabButton("Sounds", _currentSection == Sections.Sounds))
            {
                ChangeTab(Sections.Sounds);
            }

            if (DrawTabButton("Music", _currentSection == Sections.Music))
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
            _currentCompressionPreset = section == Sections.Sounds
                ? CompressionPreset.FrequentSound
                : CompressionPreset.AmbientMusic;
        }

        private void DrawTagField()
        {
            bool valid = SoundEditorHelper.IsTagValid(_currentTag);

            var previousColor = GUI.color;

            if (!string.IsNullOrEmpty(_currentTag) && !valid)
            {
                GUI.color = SoundEditorHelper.RedColor;
            }

            _currentTag = EditorGUILayout.TextField("Tag", _currentTag);

            GUI.color = previousColor;

            if (!string.IsNullOrEmpty(_currentTag) && !valid)
            {
                EditorGUILayout.HelpBox("태그는 영문/숫자만 쓸 수 있고 숫자로 시작할 수 없습니다.", MessageType.Error);
            }
        }

        private void DrawDropZone()
        {
            var dropArea = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));

            GUI.Box(dropArea, "여기에 오디오 클립을 드래그하거나 클릭해서 선택하세요", EditorStyles.helpBox);

            var evt = Event.current;

            if (!dropArea.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    _waitingForPickerResult = true;
                    EditorGUIUtility.ShowObjectPicker<AudioClip>(null, false, string.Empty, AudioClipPickerId);
                    evt.Use();
                    break;

                case EventType.DragUpdated:
                    DragAndDrop.visualMode = DragAndDrop.objectReferences.All(obj => obj is AudioClip)
                        ? DragAndDropVisualMode.Link
                        : DragAndDropVisualMode.Rejected;
                    evt.Use();
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();

                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is AudioClip clip)
                        {
                            _importedClips.Add(clip);
                        }
                    }

                    evt.Use();
                    break;
            }
        }

        private void HandleObjectPickerResult()
        {
            if (!_waitingForPickerResult) return;
            if (Event.current.commandName != "ObjectSelectorSelectionDone") return;
            if (EditorGUIUtility.GetObjectPickerControlID() != AudioClipPickerId) return;

            _waitingForPickerResult = false;

            if (EditorGUIUtility.GetObjectPickerObject() is AudioClip selectedClip)
            {
                _importedClips.Add(selectedClip);
            }

            Repaint();
        }

        private void DrawClipList()
        {
            if (_importedClips.Count == 0) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Clips ({_importedClips.Count})", EditorStyles.boldLabel);

            for (int i = 0; i < _importedClips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                _importedClips[i] =
                    (AudioClip)EditorGUILayout.ObjectField(_importedClips[i], typeof(AudioClip), false);

                if (GUILayout.Button("x", GUILayout.Width(24f)))
                {
                    _importedClips.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawImportSettings()
        {
            _currentCompressionPreset =
                (CompressionPreset)EditorGUILayout.EnumPopup("Compression Preset", _currentCompressionPreset);

            EditorGUILayout.HelpBox(GetPresetInfo(_currentCompressionPreset), MessageType.Info);

            _forceToMono = EditorGUILayout.Toggle("Force To Mono", _forceToMono);
        }

        private static string GetPresetInfo(CompressionPreset preset) => preset switch
        {
            CompressionPreset.AmbientMusic => AmbientMusicInfo,
            CompressionPreset.FrequentSound => FrequentSoundInfo,
            CompressionPreset.OccasionalSound => OccasionalSoundInfo,
            _ => string.Empty
        };

        private void DrawCreateButton()
        {
            bool canCreate = _importedClips.Count > 0 && SoundEditorHelper.IsTagValid(_currentTag);

            using (new EditorGUI.DisabledScope(!canCreate))
            {
                var previousColor = GUI.backgroundColor;

                GUI.backgroundColor = canCreate ? SoundEditorHelper.OrangeColor : previousColor;

                if (GUILayout.Button("Create", GUILayout.Height(30f)))
                {
                    CreateAudio();
                }

                GUI.backgroundColor = previousColor;
            }
        }

        private void DrawResultMessage()
        {
            if (string.IsNullOrEmpty(_resultMessage)) return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(_resultMessage, _resultIsError ? MessageType.Error : MessageType.Info);

            if (GUILayout.Button("x", GUILayout.Width(24f), GUILayout.Height(38f)))
            {
                _resultMessage = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void CreateAudio()
        {
            if (!SoundEditorHelper.IsTagValid(_currentTag))
            {
                _resultIsError = true;
                _resultMessage = "태그는 특수문자를 포함하거나 숫자로 시작할 수 없습니다.";
                return;
            }

            var settings = SoundServiceAssetLocator.GetOrCreateSettings();
            var validClips = _importedClips.Where(clip => clip != null).ToArray();

            string resultMessage;
            bool success;

            if (_currentSection == Sections.Sounds)
            {
                success = settings.SoundDataCollection.CreateSound(validClips, _currentTag,
                    _currentCompressionPreset, _forceToMono, out resultMessage);
            }
            else
            {
                success = settings.MusicDataCollection.CreateMusicTrack(validClips, _currentTag,
                    _currentCompressionPreset, _forceToMono, out resultMessage);
            }

            _resultIsError = !success;
            _resultMessage = resultMessage;

            if (!success) return;

            SoundEditorHelper.ChangeAudioClipImportSettings(validClips, _currentCompressionPreset, _forceToMono);
            SoundEditorHelper.SaveCollectionChanges(_currentSection);

            _importedClips.Clear();
            _currentTag = string.Empty;

            GUI.FocusControl(null);
        }
    }
}
