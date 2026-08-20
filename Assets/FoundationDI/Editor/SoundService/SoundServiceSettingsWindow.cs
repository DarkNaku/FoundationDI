using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// <see cref="SoundServiceSettings"/>를 편집하는 창. 데이터 경로와 오클루전 파라미터를 한곳에서 다룬다.
    /// </summary>
    public class SoundServiceSettingsWindow : EditorWindow
    {
        private SoundServiceSettings _settings;
        private SerializedObject _serializedSettings;
        private Vector2 _scroll;
        private string _dataRootPathInput;
        private string _pathError = string.Empty;

        [MenuItem("Tools/DarkNaku/FoundationDI/Sound/Settings", false, 53)]
        public static void ShowWindow()
        {
            var window = GetWindow<SoundServiceSettingsWindow>();
            window.titleContent = new GUIContent("Sound Settings");
            window.minSize = new Vector2(420f, 460f);
        }

        private void OnEnable()
        {
            Reload();
        }

        private void OnGUI()
        {
            if (_settings == null)
            {
                DrawMissingSettings();
                return;
            }

            _serializedSettings.Update();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Settings Asset", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("Asset", _settings, typeof(SoundServiceSettings), false);

            EditorGUILayout.Space(8f);
            DrawDataSection();

            EditorGUILayout.Space(8f);
            DrawOcclusionSection();

            EditorGUILayout.Space(12f);

            if (GUILayout.Button("Reset To Defaults", GUILayout.Height(24f)) &&
                EditorUtility.DisplayDialog("Reset", "설정을 기본값으로 되돌릴까요?", "Reset", "Cancel"))
            {
                Undo.RecordObject(_settings, "Reset Sound Service Settings");
                _settings.ResetToDefaults();
                EditorUtility.SetDirty(_settings);
                Reload();
            }

            EditorGUILayout.EndScrollView();

            _serializedSettings.ApplyModifiedProperties();
        }

        private void DrawMissingSettings()
        {
            EditorGUILayout.HelpBox("SoundServiceSettings 에셋이 없습니다.", MessageType.Warning);

            if (GUILayout.Button("Create Settings", GUILayout.Height(28f)))
            {
                SoundServiceAssetLocator.GetOrCreateSettings();
                Reload();
            }
        }

        private void DrawDataSection()
        {
            EditorGUILayout.LabelField("Data", EditorStyles.boldLabel);

            DrawProperty("<SoundDataCollection>k__BackingField", "Sound Collection");
            DrawProperty("<MusicDataCollection>k__BackingField", "Music Collection");
            DrawProperty("<OutputDataCollection>k__BackingField", "Output Collection");
            DrawProperty("<MasterAudioMixer>k__BackingField", "Master Audio Mixer");

            EditorGUILayout.Space(4f);

            _dataRootPathInput = EditorGUILayout.TextField("Data Root Path", _dataRootPathInput);

            if (!string.IsNullOrEmpty(_pathError))
            {
                EditorGUILayout.HelpBox(_pathError, MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(_dataRootPathInput == _settings.DataRootPath))
            {
                if (GUILayout.Button("Apply Data Root Path"))
                {
                    ApplyDataRootPath();
                }
            }
        }

        private void DrawOcclusionSection()
        {
            EditorGUILayout.LabelField("Occlusion", EditorStyles.boldLabel);

            DrawProperty("<EnableOcclusion>k__BackingField", "Enable Occlusion");

            using (new EditorGUI.DisabledScope(!_settings.EnableOcclusion))
            {
                DrawProperty("<OcclusionLayers>k__BackingField", "Occlusion Layers");
                DrawProperty("<MaxDistance>k__BackingField", "Max Distance");
                DrawProperty("<MinCutoff>k__BackingField", "Min Cutoff");
                DrawProperty("<MaxCutoff>k__BackingField", "Max Cutoff");
                DrawProperty("<MinVolumeMultiplier>k__BackingField", "Min Volume Multiplier");
                DrawProperty("<MaxBounces>k__BackingField", "Max Bounces");
                DrawProperty("<BounceRadiusMin>k__BackingField", "Bounce Radius Min");
                DrawProperty("<BounceRaysPerCircle>k__BackingField", "Bounce Rays Per Circle");
                DrawProperty("<CheckInterval>k__BackingField", "Check Interval");
                DrawProperty("<LerpSpeed>k__BackingField", "Lerp Speed");
            }
        }

        private void DrawProperty(string propertyPath, string label)
        {
            var property = _serializedSettings.FindProperty(propertyPath);

            if (property == null)
            {
                EditorGUILayout.LabelField(label, "(직렬화 필드를 찾지 못했습니다)");
                return;
            }

            EditorGUILayout.PropertyField(property, new GUIContent(label));
        }

        private void ApplyDataRootPath()
        {
            if (!TrySanitizeDataRootPath(_dataRootPathInput, out string sanitized, out _pathError)) return;

            Undo.RecordObject(_settings, "Change Data Root Path");

            _settings.DataRootPath = sanitized;

            EditorUtility.SetDirty(_settings);

            SoundServiceAssetLocator.EnsureCollections(_settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _dataRootPathInput = sanitized;
            _pathError = string.Empty;
        }

        private void Reload()
        {
            SoundServiceAssetLocator.ClearCache();

            _settings = SoundServiceAssetLocator.FindSettings();

            if (_settings == null) return;

            _serializedSettings = new SerializedObject(_settings);
            _dataRootPathInput = _settings.DataRootPath;
            _pathError = string.Empty;
        }

        /// <summary>프로젝트 상대 경로로만 데이터 루트를 허용한다.</summary>
        internal static bool TrySanitizeDataRootPath(string input, out string sanitized, out string error)
        {
            sanitized = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "경로가 비어 있습니다.";
                return false;
            }

            string path = Regex.Replace(input.Trim().Replace("\\", "/"), "/+", "/");

            if (path.Contains(":/") || path.StartsWith("/") || path.StartsWith("~"))
            {
                error = "경로는 Assets/ 아래의 프로젝트 상대 경로여야 합니다.";
                return false;
            }

            if (!path.StartsWith("Assets/") && !path.Equals("Assets"))
            {
                path = "Assets/" + path.TrimStart('/');
            }

            var segments = path.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0 || segments[0] != "Assets")
            {
                error = "경로는 Assets/로 시작해야 합니다.";
                return false;
            }

            for (int i = 1; i < segments.Length; i++)
            {
                string segment = segments[i];

                if (segment is "." or "..")
                {
                    error = "경로에 '.' 또는 '..' 세그먼트를 쓸 수 없습니다.";
                    return false;
                }

                if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    !Regex.IsMatch(segment, @"^[a-zA-Z0-9 _\-\.]+$"))
                {
                    error = $"폴더 이름 '{segment}'에 쓸 수 없는 문자가 있습니다.";
                    return false;
                }

                if (segment.Length > 60)
                {
                    error = $"폴더 이름 '{segment}'이(가) 너무 깁니다.";
                    return false;
                }
            }

            sanitized = string.Join("/", segments) + "/";

            if (sanitized.Length > 200)
            {
                error = "경로가 너무 깁니다.";
                return false;
            }

            return true;
        }
    }
}
