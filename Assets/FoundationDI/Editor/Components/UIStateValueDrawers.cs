using DarkNaku.FoundationDI;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomPropertyDrawer(typeof(UIImageStateValue))]
    public class UIImageStateValueDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var flags = (UIImageSwap)property.FindPropertyRelative("Override").intValue;

            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, property.FindPropertyRelative("Override"), label);

            EditorGUI.indentLevel++;
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            if ((flags & UIImageSwap.Sprite) != 0)
            {
                EditorGUI.PropertyField(line, property.FindPropertyRelative("Sprite"));
                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            if ((flags & UIImageSwap.Color) != 0)
            {
                EditorGUI.PropertyField(line, property.FindPropertyRelative("Color"));
                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            if ((flags & UIImageSwap.Visible) != 0)
            {
                EditorGUI.PropertyField(line, property.FindPropertyRelative("Visible"));
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var flags = (UIImageSwap)property.FindPropertyRelative("Override").intValue;

            int lines = 1;
            if ((flags & UIImageSwap.Sprite) != 0) lines++;
            if ((flags & UIImageSwap.Color) != 0) lines++;
            if ((flags & UIImageSwap.Visible) != 0) lines++;

            return lines * EditorGUIUtility.singleLineHeight
                 + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }
    }

    [CustomPropertyDrawer(typeof(UITextStateValue))]
    public class UITextStateValueDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var flags = (UITextSwap)property.FindPropertyRelative("Override").intValue;

            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, property.FindPropertyRelative("Override"), label);

            EditorGUI.indentLevel++;
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            if ((flags & UITextSwap.Text) != 0)
            {
                EditorGUI.PropertyField(line, property.FindPropertyRelative("Text"));
                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            if ((flags & UITextSwap.Color) != 0)
            {
                EditorGUI.PropertyField(line, property.FindPropertyRelative("Color"));
                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            if ((flags & UITextSwap.Material) != 0)
            {
                EditorGUI.PropertyField(line, property.FindPropertyRelative("Material"));
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var flags = (UITextSwap)property.FindPropertyRelative("Override").intValue;

            int lines = 1;
            if ((flags & UITextSwap.Text) != 0) lines++;
            if ((flags & UITextSwap.Color) != 0) lines++;
            if ((flags & UITextSwap.Material) != 0) lines++;

            return lines * EditorGUIUtility.singleLineHeight
                 + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
