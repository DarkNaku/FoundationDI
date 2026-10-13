using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(ConsentServiceSettings))]
    internal sealed class ConsentServiceSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var changed = EditorGUI.EndChangeCheck();

            var settings = (ConsentServiceSettings)target;

            if (settings.UseUmp && !UmpDependenciesSync.IsEdm4uInstalled())
            {
                EditorGUILayout.HelpBox("External Dependency Manager(EDM4U)가 없다. UMP 네이티브 의존성이 빌드에 들어가지 " +
                                        "않는다. LevelPlay를 설치했다면 함께 들어 있다.", MessageType.Warning);
            }

            if (settings.UseUmp && string.IsNullOrEmpty(settings.AdMobAppIdAndroid) && string.IsNullOrEmpty(settings.AdMobAppIdIos))
            {
                EditorGUILayout.HelpBox("AdMob 앱 ID가 비어 있다. LevelPlay AdMob 어댑터가 넣지 않았다면 빌드가 멈춘다.",
                                        MessageType.Info);
            }

            if (settings.PromptPrefab == null)
            {
                EditorGUILayout.HelpBox("Prompt Prefab이 비어 있다. 약관 창을 띄울 수 없어 약관 수락이 되지 않는다.",
                                        MessageType.Error);
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(settings);
                UmpDependenciesSync.Run();
            }
        }
    }
}
