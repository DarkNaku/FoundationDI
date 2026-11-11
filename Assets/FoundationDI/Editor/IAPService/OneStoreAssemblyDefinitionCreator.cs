using System.IO;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // 원스토어 Unity 플러그인에는 asmdef가 없어 Assembly-CSharp로 컴파일되고, 패키지(FoundationDI.OneStore)는
    // Assembly-CSharp를 참조할 수 없다. 플러그인 폴더(OneStoreCorpPlugins)에 이 이름의 asmdef를 만들어 잇는다.
    // FOUNDATIONDI_ONESTORE 심볼보다 먼저 실행해야 한다(심볼이 켜졌는데 asmdef가 없으면 SDK 타입을 못 찾는다).
    public static class OneStoreAssemblyDefinitionCreator
    {
        public const string AssemblyName = "OneStoreCorpPlugins";

        private const string MarkerScript = "PurchaseClientImpl";

        [MenuItem("Tools/FoundationDI/IAP/Create OneStore Assembly Definition")]
        public static void Create()
        {
            var folder = FindPluginFolder();

            if (folder == null)
            {
                EditorUtility.DisplayDialog("OneStore", "원스토어 플러그인(OneStoreCorpPlugins 폴더)을 찾지 못했다. " +
                                                        "원스토어 unitypackage를 먼저 가져올 것.", "OK");
                return;
            }

            var path = $"{folder}/{AssemblyName}.asmdef";

            if (File.Exists(path))
            {
                Debug.Log($"[OneStoreIAPService] asmdef가 이미 있다: {path}");
                return;
            }

            File.WriteAllText(path, BuildJson());
            AssetDatabase.ImportAsset(path);

            Debug.Log($"[OneStoreIAPService] asmdef를 만들었다: {path}. 이제 FOUNDATIONDI_ONESTORE 심볼을 켤 수 있다.");
        }

        // PurchaseClientImpl.cs가 있는 폴더에서 위로 올라가며 OneStoreCorpPlugins 폴더를 찾는다.
        internal static string FindPluginFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets($"{MarkerScript} t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != MarkerScript) continue;

                var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');

                while (!string.IsNullOrEmpty(folder))
                {
                    if (Path.GetFileName(folder) == AssemblyName) return folder;

                    folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                }
            }

            return null;
        }

        // 플러그인의 스크립트는 스스로 #if UNITY_ANDROID || !UNITY_EDITOR로 감싸여 있어 모든 플랫폼에 둔다.
        internal static string BuildJson() =>
            "{\n" +
            $"    \"name\": \"{AssemblyName}\",\n" +
            "    \"rootNamespace\": \"\",\n" +
            "    \"references\": [],\n" +
            "    \"includePlatforms\": [],\n" +
            "    \"excludePlatforms\": [],\n" +
            "    \"allowUnsafeCode\": false,\n" +
            "    \"overrideReferences\": false,\n" +
            "    \"precompiledReferences\": [],\n" +
            "    \"autoReferenced\": true,\n" +
            "    \"defineConstraints\": [],\n" +
            "    \"versionDefines\": [],\n" +
            "    \"noEngineReferences\": false\n" +
            "}\n";
    }
}
