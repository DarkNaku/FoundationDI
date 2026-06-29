using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(SoundButton))]
    public sealed class SoundButtonEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var catalogProp = serializedObject.FindProperty("_catalog");
            var keyProp = serializedObject.FindProperty("_key");

            EditorGUILayout.PropertyField(catalogProp);

            var catalog = catalogProp.objectReferenceValue as SoundCatalog;
            var keys = catalog != null ? new List<string>(catalog.Keys) : null;

            if (keys != null && keys.Count > 0)
            {
                int current = Mathf.Max(0, keys.IndexOf(keyProp.stringValue));
                int selected = EditorGUILayout.Popup("Key", current, keys.ToArray());
                keyProp.stringValue = keys[selected];
            }
            else
            {
                EditorGUILayout.PropertyField(keyProp);
            }

            DrawPropertiesExcluding(serializedObject, "m_Script", "_catalog", "_key");

            serializedObject.ApplyModifiedProperties();
        }
    }
}
