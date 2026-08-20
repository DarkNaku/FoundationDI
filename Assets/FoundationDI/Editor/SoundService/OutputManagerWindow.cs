using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// AudioMixer의 그룹을 Output 데이터베이스로 읽어 오고, 각 Output의 볼륨을 미리 조절해 보는 창.
    /// </summary>
    public class OutputManagerWindow : EditorWindow
    {
        private enum WindowLabel
        {
            Main,
            CreateOutput
        }

        private static readonly string[] Instructions =
        {
            "1. Audio Mixer 창을 열고 Master 믹서를 선택한다.",
            "2. Groups 패널에서 '+'로 새 그룹을 추가한다.",
            "3. 그룹 이름을 원하는 Output 이름으로 바꾼다(공백은 제거된다).",
            "4. 그룹을 선택한 뒤 Inspector에서 Volume을 우클릭 → 'Expose ... to script'.",
            "5. Audio Mixer 창 오른쪽 위 'Exposed Parameters'에서 노출된 파라미터를 그룹 이름과 똑같이 바꾼다.",
            "6. 이 창으로 돌아와 'Reload Outputs'를 누른다."
        };

        private WindowLabel _currentLabel = WindowLabel.Main;
        private Vector2 _scroll;

        private SoundService _service;

        [MenuItem("Tools/FoundationDI/Sound/Output Manager", false, 52)]
        public static void ShowWindow()
        {
            var window = GetWindow<OutputManagerWindow>();
            window.titleContent = new GUIContent("Output Manager");
            window.minSize = new Vector2(380f, 360f);
        }

        private void OnDisable()
        {
            _service?.Dispose();
            _service = null;
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (_currentLabel == WindowLabel.CreateOutput)
                {
                    DrawCreateOutputLabel();
                    return;
                }

                DrawMainLabel();
            }
        }

        private void DrawMainLabel()
        {
            var settings = SoundServiceAssetLocator.GetOrCreateSettings();

            settings.MasterAudioMixer = (UnityEngine.Audio.AudioMixer)EditorGUILayout.ObjectField(
                "Master Audio Mixer", settings.MasterAudioMixer, typeof(UnityEngine.Audio.AudioMixer), false);

            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reload Outputs", GUILayout.Height(24f)))
            {
                SoundEditorHelper.ReloadOutputsDatabase();
            }

            if (GUILayout.Button("Open Audio Mixer", GUILayout.Height(24f)))
            {
                OpenAudioMixer(settings);
            }

            if (GUILayout.Button("How to add an Output", GUILayout.Height(24f)))
            {
                _currentLabel = WindowLabel.CreateOutput;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);

            var outputs = settings.OutputDataCollection.Outputs;

            if (outputs.Length == 0)
            {
                EditorGUILayout.HelpBox("Output이 없습니다. 믹서를 지정하고 'Reload Outputs'를 누르세요.",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            _service ??= new SoundService(settings);

            for (int i = 0; i < outputs.Length; i++)
            {
                var outputData = outputs[i];

                if (outputData.Output == null || outputData.Output.audioMixer == null) continue;

                bool exposed = outputData.Output.audioMixer.GetFloat(outputData.Name, out _);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(outputData.Name, i == 0 ? EditorStyles.boldLabel : EditorStyles.label,
                    GUILayout.Width(150f));

                if (!exposed)
                {
                    EditorGUILayout.LabelField("Volume 파라미터가 노출되지 않았습니다.", EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                    continue;
                }

                float volume = _service.GetSavedOutputVolume(outputData.Name);
                float newVolume = EditorGUILayout.Slider(volume, 0f, 1f);

                if (!Mathf.Approximately(volume, newVolume))
                {
                    _service.ChangeOutputVolume(outputData.Name, newVolume);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawCreateOutputLabel()
        {
            if (GUILayout.Button("< Back", GUILayout.Width(80f)))
            {
                _currentLabel = WindowLabel.Main;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("새 Output 만들기", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var instruction in Instructions)
            {
                EditorGUILayout.LabelField(instruction, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6f);

            if (GUILayout.Button("Open Audio Mixer", GUILayout.Height(24f)))
            {
                OpenAudioMixer(SoundServiceAssetLocator.GetOrCreateSettings());
            }

            if (GUILayout.Button("Reload Outputs", GUILayout.Height(24f)))
            {
                SoundEditorHelper.ReloadOutputsDatabase();
                _currentLabel = WindowLabel.Main;
            }
        }

        private static void OpenAudioMixer(SoundServiceSettings settings)
        {
            var mixer = settings.MasterAudioMixer;

            if (mixer == null)
            {
                Debug.LogWarning("[SoundService] Master AudioMixer가 지정되지 않았습니다.");
                return;
            }

            EditorApplication.ExecuteMenuItem("Window/Audio/Audio Mixer");

            Selection.activeObject = mixer;

            EditorGUIUtility.PingObject(mixer);
        }
    }
}
