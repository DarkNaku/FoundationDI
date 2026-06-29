using System.Collections.Generic;
using System.Linq;
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

            var catalogs = FindCatalogs();
            var current = catalogProp.objectReferenceValue as SoundCatalogSO;

            if (catalogs.Count == 0)
            {
                EditorGUILayout.HelpBox("프로젝트에 SoundCatalogSO 에셋이 없습니다.", MessageType.Info);
            }
            else
            {
                // 프로젝트에 카탈로그가 하나뿐이고 미할당이면 자동 참조.
                if (current == null && catalogs.Count == 1)
                {
                    current = catalogs[0];
                    catalogProp.objectReferenceValue = current;
                }

                // 프로젝트 전체 카탈로그 중에서 선택(직접 에셋 드래그 대신 드롭다운).
                var options = new List<string> { "(없음)" };
                options.AddRange(catalogs.Select(c => c.name));

                int index = current != null ? catalogs.IndexOf(current) + 1 : 0;
                int selected = EditorGUILayout.Popup("Catalog", index, options.ToArray());

                catalogProp.objectReferenceValue = selected == 0 ? null : catalogs[selected - 1];
                current = catalogProp.objectReferenceValue as SoundCatalogSO;
            }

            // 카탈로그가 할당된 경우에만 Key 선택 UI를 노출한다.
            if (current != null)
            {
                var keys = new List<string>(current.Keys);
                if (keys.Count > 0)
                {
                    int currentKey = Mathf.Max(0, keys.IndexOf(keyProp.stringValue));
                    int selectedKey = EditorGUILayout.Popup("Key", currentKey, keys.ToArray());
                    keyProp.stringValue = keys[selectedKey];
                }
                else
                {
                    EditorGUILayout.HelpBox("카탈로그에 키가 없습니다.", MessageType.Info);
                }
            }

            DrawPropertiesExcluding(serializedObject, "m_Script", "_catalog", "_key");

            serializedObject.ApplyModifiedProperties();
        }

        private static List<SoundCatalogSO> FindCatalogs()
        {
            return AssetDatabase.FindAssets("t:SoundCatalogSO")
                .Select(guid => AssetDatabase.LoadAssetAtPath<SoundCatalogSO>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(catalog => catalog != null)
                .ToList();
        }
    }
}
