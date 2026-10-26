using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(ReviewServiceSettings))]
    internal sealed class ReviewServiceSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var changed = EditorGUI.EndChangeCheck();

            var settings = (ReviewServiceSettings)target;

            if (settings.UsePlayInAppReview && !UmpDependenciesSync.IsEdm4uInstalled())
            {
                EditorGUILayout.HelpBox("External Dependency Manager(EDM4U)가 없다. Play In-App Review 라이브러리가 " +
                                        "빌드에 들어가지 않는다. LevelPlay를 설치했다면 함께 들어 있다.", MessageType.Warning);
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(settings);
                PlayReviewDependenciesSync.Run();
            }
        }
    }
}
