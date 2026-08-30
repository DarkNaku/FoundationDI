using DarkNaku.FoundationDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(UIStateButton), true)]
    [CanEditMultipleObjects]
    public class UIStateButtonEditor : UIButtonEditor
    {
        private SerializedProperty _imageSets;
        private SerializedProperty _textSets;
        private SerializedProperty _deselectOnClick;
        private SerializedProperty _transition;

        protected override void OnEnable()
        {
            base.OnEnable();

            _imageSets = serializedObject.FindProperty("_imageSets");
            _textSets = serializedObject.FindProperty("_textSets");
            _deselectOnClick = serializedObject.FindProperty("_deselectOnClick");
            _transition = serializedObject.FindProperty("m_Transition");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("State Swap", EditorStyles.boldLabel);

            DrawTransitionWarning();
            DrawEmptyTargetWarning();

            EditorGUILayout.PropertyField(_imageSets, true);
            EditorGUILayout.PropertyField(_textSets, true);
            EditorGUILayout.PropertyField(_deselectOnClick);

            serializedObject.ApplyModifiedProperties();
        }

        private bool HasAnySet() => _imageSets.arraySize > 0 || _textSets.arraySize > 0;

        private void DrawTransitionWarning()
        {
            if (!HasAnySet()) return;
            if (_transition == null) return;
            if (_transition.enumValueIndex == (int)Selectable.Transition.None) return;

            EditorGUILayout.HelpBox(
                "스왑 세트가 있는데 Transition이 None이 아닙니다. " +
                "내장 ColorTint/SpriteSwap이 targetGraphic에 이중으로 적용됩니다. " +
                "Transition을 None으로 두는 것을 권장합니다.",
                MessageType.Warning);
        }

        private void DrawEmptyTargetWarning()
        {
            if (HasEmptyTarget(_imageSets) || HasEmptyTarget(_textSets))
            {
                EditorGUILayout.HelpBox(
                    "Target이 비어 있는 세트가 있습니다. 그 세트는 아무 일도 하지 않습니다.",
                    MessageType.Warning);
            }
        }

        private static bool HasEmptyTarget(SerializedProperty list)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                var target = list.GetArrayElementAtIndex(i).FindPropertyRelative("Target");
                if (target != null && target.objectReferenceValue == null) return true;
            }
            return false;
        }

        // Normal이 오버라이드하지 않는 필드를 다른 상태가 오버라이드하면, 그 상태를 벗어나
    }
}
