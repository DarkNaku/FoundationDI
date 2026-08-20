using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>선택한 도형/재생 모드에 해당하는 항목만 보여 주는 <see cref="MusicZone"/> 인스펙터.</summary>
    [CustomEditor(typeof(MusicZone))]
    [CanEditMultipleObjects]
    public class MusicZoneEditor : UnityEditor.Editor
    {
        private SerializedProperty _zoneShape;
        private SerializedProperty _useScaleAsZoneSize;
        private SerializedProperty _drawWireframe;
        private SerializedProperty _radius;
        private SerializedProperty _extraRadiusFade;
        private SerializedProperty _height;
        private SerializedProperty _width;
        private SerializedProperty _depth;
        private SerializedProperty _extraBoxSizeFade;
        private SerializedProperty _areaColor;
        private SerializedProperty _fadeColor;
        private SerializedProperty _playerMode;
        private SerializedProperty _tracks;
        private SerializedProperty _volume;
        private SerializedProperty _dynamicTracks;
        private SerializedProperty _loop;
        private SerializedProperty _output;

        private void OnEnable()
        {
            _zoneShape = serializedObject.FindProperty(nameof(MusicZone.zoneShape));
            _useScaleAsZoneSize = serializedObject.FindProperty(nameof(MusicZone.useScaleAsZoneSize));
            _drawWireframe = serializedObject.FindProperty(nameof(MusicZone.drawWireframe));
            _radius = serializedObject.FindProperty(nameof(MusicZone.radius));
            _extraRadiusFade = serializedObject.FindProperty(nameof(MusicZone.extraRadiusFade));
            _height = serializedObject.FindProperty(nameof(MusicZone.height));
            _width = serializedObject.FindProperty(nameof(MusicZone.width));
            _depth = serializedObject.FindProperty(nameof(MusicZone.depth));
            _extraBoxSizeFade = serializedObject.FindProperty(nameof(MusicZone.extraBoxSizeFade));
            _areaColor = serializedObject.FindProperty(nameof(MusicZone.areaColor));
            _fadeColor = serializedObject.FindProperty(nameof(MusicZone.fadeColor));
            _playerMode = serializedObject.FindProperty(nameof(MusicZone.playerMode));
            _tracks = serializedObject.FindProperty(nameof(MusicZone.tracks));
            _volume = serializedObject.FindProperty(nameof(MusicZone.volume));
            _dynamicTracks = serializedObject.FindProperty(nameof(MusicZone.dynamicTracks));
            _loop = serializedObject.FindProperty(nameof(MusicZone.loop));
            _output = serializedObject.FindProperty(nameof(MusicZone.output));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_zoneShape);
            EditorGUILayout.PropertyField(_useScaleAsZoneSize);

            bool useScale = _useScaleAsZoneSize.boolValue;

            if ((MusicZone.Shape)_zoneShape.enumValueIndex == MusicZone.Shape.Sphere)
            {
                using (new EditorGUI.DisabledScope(useScale))
                {
                    EditorGUILayout.PropertyField(_radius);
                }

                EditorGUILayout.PropertyField(_extraRadiusFade);
            }
            else
            {
                using (new EditorGUI.DisabledScope(useScale))
                {
                    EditorGUILayout.PropertyField(_width);
                    EditorGUILayout.PropertyField(_height);
                    EditorGUILayout.PropertyField(_depth);
                }

                EditorGUILayout.PropertyField(_extraBoxSizeFade);
            }

            EditorGUILayout.PropertyField(_drawWireframe);
            EditorGUILayout.PropertyField(_areaColor);
            EditorGUILayout.PropertyField(_fadeColor);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Music", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_playerMode);

            var mode = (MusicZone.PlayerMode)_playerMode.enumValueIndex;

            if (mode == MusicZone.PlayerMode.DynamicMusic)
            {
                EditorGUILayout.PropertyField(_dynamicTracks, new GUIContent("Dynamic Tracks"), true);
            }
            else
            {
                EditorGUILayout.PropertyField(_tracks,
                    new GUIContent(mode == MusicZone.PlayerMode.Music ? "Track" : "Tracks"), true);
                EditorGUILayout.PropertyField(_volume);

                if (mode == MusicZone.PlayerMode.Music && _tracks.arraySize > 1)
                {
                    EditorGUILayout.HelpBox("Music 모드에서는 첫 번째 트랙만 재생됩니다.", MessageType.Info);
                }
            }

            EditorGUILayout.PropertyField(_loop);
            EditorGUILayout.PropertyField(_output);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
