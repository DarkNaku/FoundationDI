using DarkNaku.FoundationDI;
using UnityEditor;
using UnityEditor.UI;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(UIButton), true)]
    [CanEditMultipleObjects]
    public class UIButtonEditor : ButtonEditor
    {
        private SerializedProperty _sfx;
        private SerializedProperty _output;
        private SerializedProperty _volume;
        private SerializedProperty _randomPitch;
        private SerializedProperty _useHaptic;
        private SerializedProperty _hapticImpact;

        protected override void OnEnable()
        {
            base.OnEnable();

            _sfx = serializedObject.FindProperty("_sfx");
            _output = serializedObject.FindProperty("_output");
            _volume = serializedObject.FindProperty("_volume");
            _randomPitch = serializedObject.FindProperty("_randomPitch");
            _useHaptic = serializedObject.FindProperty("_useHaptic");
            _hapticImpact = serializedObject.FindProperty("_hapticImpact");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();
            DrawFeedback();
            serializedObject.ApplyModifiedProperties();
        }

        protected void DrawFeedback()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sound", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sfx);
            EditorGUILayout.PropertyField(_output);
            EditorGUILayout.PropertyField(_volume);
            EditorGUILayout.PropertyField(_randomPitch);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Haptic", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_useHaptic);

            using (new EditorGUI.DisabledScope(!_useHaptic.boolValue))
            {
                EditorGUILayout.PropertyField(_hapticImpact);
            }
        }
    }
}
