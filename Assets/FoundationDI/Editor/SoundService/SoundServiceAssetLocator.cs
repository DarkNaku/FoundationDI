using System.IO;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// 에디터 도구가 <see cref="SoundServiceSettings"/>와 데이터 에셋을 찾고, 없으면 만들어 주는 헬퍼.
    /// 런타임은 DI로 설정을 받으므로 Resources에 의존하지 않는다.
    /// </summary>
    internal static class SoundServiceAssetLocator
    {
        internal const string DefaultDataRootPath = "Assets/FoundationDI.Data/SoundService/";
        internal const string RuntimeAssemblyName = "FoundationDI";

        private static SoundServiceSettings _cachedSettings;

        /// <summary>설정 에셋을 찾는다. 없으면 null(생성하지 않음).</summary>
        internal static SoundServiceSettings FindSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;

            var guids = AssetDatabase.FindAssets($"t:{nameof(SoundServiceSettings)}");

            if (guids.Length == 0) return null;

            if (guids.Length > 1)
            {
                Debug.LogWarning($"[SoundService] SoundServiceSettings 에셋이 {guids.Length}개 있습니다. " +
                                 "첫 번째 항목을 사용합니다.");
            }

            _cachedSettings =
                AssetDatabase.LoadAssetAtPath<SoundServiceSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));

            return _cachedSettings;
        }

        /// <summary>설정 에셋을 찾고, 없으면 기본 경로에 설정과 데이터 에셋 일체를 생성한다.</summary>
        internal static SoundServiceSettings GetOrCreateSettings()
        {
            var settings = FindSettings();

            if (settings != null)
            {
                EnsureCollections(settings);
                return settings;
            }

            EnsureFolder(DefaultDataRootPath);

            settings = ScriptableObject.CreateInstance<SoundServiceSettings>();
            settings.DataRootPath = DefaultDataRootPath;

            AssetDatabase.CreateAsset(settings, DefaultDataRootPath + "SoundServiceSettings.asset");

            _cachedSettings = settings;

            EnsureCollections(settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SoundService] 기본 설정을 생성했습니다: {DefaultDataRootPath}SoundServiceSettings.asset");

            return settings;
        }

        /// <summary>설정이 참조하는 컬렉션 에셋이 없으면 만들어 연결한다.</summary>
        internal static void EnsureCollections(SoundServiceSettings settings)
        {
            string root = settings.GetNormalizedDataRootPath();
            string collectionsPath = root + "Collections/";

            EnsureFolder(collectionsPath);

            bool dirty = false;

            if (settings.SoundDataCollection == null)
            {
                settings.SoundDataCollection =
                    CreateOrLoad<SoundDataCollection>(collectionsPath + "SoundCollection.asset");
                dirty = true;
            }

            if (settings.MusicDataCollection == null)
            {
                settings.MusicDataCollection =
                    CreateOrLoad<MusicDataCollection>(collectionsPath + "MusicCollection.asset");
                dirty = true;
            }

            if (settings.OutputDataCollection == null)
            {
                settings.OutputDataCollection =
                    CreateOrLoad<OutputDataCollection>(collectionsPath + "OutputCollection.asset");
                dirty = true;
            }

            if (!dirty) return;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>생성된 유사 enum 코드가 놓일 폴더. asmref도 함께 보장한다.</summary>
        internal static string GetGeneratedFolder(SoundServiceSettings settings)
        {
            string generatedPath = settings.GetNormalizedDataRootPath() + "Generated/";

            EnsureFolder(generatedPath);
            EnsureAssemblyReference(generatedPath);

            return generatedPath;
        }

        internal static void ClearCache() => _cachedSettings = null;

        /// <summary>
        /// 생성된 코드가 런타임 어셈블리(FoundationDI)에 포함되도록 asmref를 놓는다.
        /// 유사 enum은 partial struct라 반드시 같은 어셈블리에 있어야 한다.
        /// </summary>
        private static void EnsureAssemblyReference(string generatedFolder)
        {
            string asmrefPath = generatedFolder + RuntimeAssemblyName + ".asmref";

            if (File.Exists(asmrefPath)) return;

            File.WriteAllText(asmrefPath, "{\n    \"reference\": \"" + RuntimeAssemblyName + "\"\n}");

            AssetDatabase.ImportAsset(asmrefPath);
        }

        private static T CreateOrLoad<T>(string assetPath) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);

            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();

            AssetDatabase.CreateAsset(created, assetPath);

            return created;
        }

        private static void EnsureFolder(string path)
        {
            string trimmed = path.TrimEnd('/');

            if (AssetDatabase.IsValidFolder(trimmed)) return;

            var segments = trimmed.Split('/');
            string current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];

                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }
    }
}
