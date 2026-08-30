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
            DrawUnrestoredOverrideWarning();

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
        // Normal로 되돌아갈 때 TryResolve의 세 번째 분기("아무것도 쓰지 않는다")를 타서
        // 값이 원래대로 복원되지 않는다. 런타임 폴백 로직 자체는 바꾸지 않고(별도 스펙 대기),
        // 인스펙터에서 이 조합을 미리 경고만 한다.
        private void DrawUnrestoredOverrideWarning()
        {
            if (HasUnrestoredOverride(_imageSets) || HasUnrestoredOverride(_textSets))
            {
                EditorGUILayout.HelpBox(
                    "Normal이 오버라이드하지 않는 필드를 다른 상태가 오버라이드하고 있습니다. " +
                    "그 상태를 벗어나도 원래 값으로 돌아오지 않습니다. " +
                    "Normal에서도 같은 필드를 오버라이드하세요.",
                    MessageType.Warning);
            }
        }

        private static bool HasUnrestoredOverride(SerializedProperty list)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);

                var normalMask = GetOverrideMask(element, "Normal");
                var otherMask = GetOverrideMask(element, "Highlighted")
                    | GetOverrideMask(element, "Pressed")
                    | GetOverrideMask(element, "Selected")
                    | GetOverrideMask(element, "Disabled");

                if ((otherMask & ~normalMask) != 0) return true;
            }
            return false;
        }

        private static int GetOverrideMask(SerializedProperty element, string stateName)
        {
            var overrideProp = element.FindPropertyRelative(stateName)?.FindPropertyRelative("Override");
            return overrideProp != null ? overrideProp.intValue : 0;
        }
    }
}
