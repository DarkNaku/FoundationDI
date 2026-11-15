using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(AppUpdateServiceSettings))]
    internal sealed class AppUpdateServiceSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var changed = EditorGUI.EndChangeCheck();

            var settings = (AppUpdateServiceSettings)target;

            if (settings.UsePlayAppUpdate && !UmpDependenciesSync.IsEdm4uInstalled())
            {
                EditorGUILayout.HelpBox("External Dependency Manager(EDM4U)가 없다. Play In-App Updates 라이브러리가 " +
                                        "빌드에 들어가지 않는다. LevelPlay를 설치했다면 함께 들어 있다.", MessageType.Warning);
            }

            if (string.IsNullOrWhiteSpace(settings.AppStoreCountry))
            {
                EditorGUILayout.HelpBox("App Store Country가 비어 있어 iOS는 미국 스토어를 조회한다. 미국에 출시하지 않았다면 " +
                                        "늘 Unknown이 된다. 출시 국가 코드(예: kr)를 넣는다.", MessageType.Info);
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(settings);
                PlayAppUpdateDependenciesSync.Run();
            }
        }
    }
}
