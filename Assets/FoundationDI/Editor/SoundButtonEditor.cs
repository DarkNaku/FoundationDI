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

            // 카탈로그가 할당된 경우에만 Key 선택 UI를 노출한다.
            // 미할당 시 Key 입력 필드는 혼란만 주므로 그리지 않는다.
            var catalog = catalogProp.objectReferenceValue as SoundCatalogSO;
            if (catalog != null)
            {
                var keys = new List<string>(catalog.Keys);
                if (keys.Count > 0)
                {
                    int current = Mathf.Max(0, keys.IndexOf(keyProp.stringValue));
                    int selected = EditorGUILayout.Popup("Key", current, keys.ToArray());
                    keyProp.stringValue = keys[selected];
                }
                else
                {
                    EditorGUILayout.HelpBox("카탈로그에 키가 없습니다.", MessageType.Info);
                }
            }

            DrawPropertiesExcluding(serializedObject, "m_Script", "_catalog", "_key");

            serializedObject.ApplyModifiedProperties();
        }
    }
}
