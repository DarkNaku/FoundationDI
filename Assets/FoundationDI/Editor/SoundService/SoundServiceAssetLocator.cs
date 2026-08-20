using System.Collections.Generic;
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

        private const string GeneratedSuffix = "_Generated.cs";

        private static SoundServiceSettings _cachedSettings;
        private static bool _ambiguityHintShown;

        /// <summary>
        /// 에디터 도구가 편집 대상으로 삼을 설정 에셋. 프로젝트에 설정이 여러 개일 때
        /// (예: 샘플이 자체 설정을 들고 올 때) 무엇을 편집할지 명시적으로 고른다.
        /// EditorPrefs에 프로젝트별로 저장된다.
        /// </summary>
        internal static SoundServiceSettings ActiveSettings
        {
            get
            {
                string guid = EditorPrefs.GetString(ActiveSettingsPrefKey, string.Empty);

                if (string.IsNullOrEmpty(guid)) return null;

                string path = AssetDatabase.GUIDToAssetPath(guid);

                return string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SoundServiceSettings>(path);
            }
            set
            {
                _cachedSettings = null;
                _ambiguityHintShown = false;

                if (value == null)
                {
                    EditorPrefs.DeleteKey(ActiveSettingsPrefKey);
                    return;
                }

                EditorPrefs.SetString(ActiveSettingsPrefKey,
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)));
            }
        }

        private static string ActiveSettingsPrefKey =>
            $"DarkNaku.FoundationDI.SoundService.ActiveSettings.{Application.dataPath.GetHashCode():X}";

        /// <summary>프로젝트에 있는 모든 설정 에셋을 경로 순으로 반환한다.</summary>
        internal static SoundServiceSettings[] FindAllSettings()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(SoundServiceSettings)}");
            var settings = new List<SoundServiceSettings>(guids.Length);

            foreach (var guid in guids)
            {
                var asset =
                    AssetDatabase.LoadAssetAtPath<SoundServiceSettings>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null) settings.Add(asset);
            }

            settings.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a),
                AssetDatabase.GetAssetPath(b)));

            return settings.ToArray();
        }

        /// <summary>
        /// 편집 대상 설정 에셋을 찾는다. 없으면 null(생성하지 않음).
        /// <see cref="ActiveSettings"/>가 지정되어 있으면 그것을, 아니면 프로젝트에 하나뿐인 것을 쓴다.
        /// 여러 개인데 고르지 않았다면 첫 번째를 쓰고 한 번만 안내를 남긴다.
        /// </summary>
        internal static SoundServiceSettings FindSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;

            var active = ActiveSettings;

            if (active != null)
            {
                _cachedSettings = active;
                return _cachedSettings;
            }

            var all = FindAllSettings();

            if (all.Length == 0) return null;

            if (all.Length > 1 && !_ambiguityHintShown)
            {
                _ambiguityHintShown = true;

                Debug.LogWarning($"[SoundService] SoundServiceSettings 에셋이 {all.Length}개 있습니다. " +
                                 $"'{AssetDatabase.GetAssetPath(all[0])}'를 사용합니다. " +
                                 "Tools > FoundationDI > Sound > Settings 창에서 편집 대상을 고를 수 있습니다.");
            }

            _cachedSettings = all[0];

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

        /// <summary>
        /// 생성된 유사 enum 코드가 놓일 폴더. asmref도 함께 보장한다.
        /// 유사 enum은 partial struct라 프로젝트 전체에 한 벌만 존재할 수 있으므로,
        /// 다른 위치에 남아 있는 생성 폴더는 정리한다.
        /// </summary>
        internal static string GetGeneratedFolder(SoundServiceSettings settings)
        {
            string generatedPath = settings.GetNormalizedDataRootPath() + "Generated/";

            EnsureFolder(generatedPath);
            EnsureAssemblyReference(generatedPath);
            RemoveStaleGeneratedFolders(generatedPath);

            return generatedPath;
        }

        /// <summary>
        /// 대상 폴더가 아닌 곳에 있는 생성 코드를 지운다.
        /// 두 벌이 공존하면 같은 상수가 중복 정의되어 컴파일이 깨진다.
        /// 생성 파일은 컬렉션에서 언제든 다시 만들어지므로 지워도 안전하다.
        /// </summary>
        private static void RemoveStaleGeneratedFolders(string keepFolder)
        {
            string[] generatedNames =
            {
                nameof(SFX) + GeneratedSuffix,
                nameof(Track) + GeneratedSuffix,
                nameof(Output) + GeneratedSuffix
            };

            var removed = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets($"{RuntimeAssemblyName} t:AssemblyDefinitionReferenceAsset"))
            {
                string asmrefPath = AssetDatabase.GUIDToAssetPath(guid);
                string folder = asmrefPath[..(asmrefPath.LastIndexOf('/') + 1)];

                if (folder == keepFolder) continue;

                bool holdsGeneratedCode = false;

                foreach (var name in generatedNames)
                {
                    string filePath = folder + name;

                    if (!File.Exists(filePath)) continue;

                    AssetDatabase.DeleteAsset(filePath);
                    holdsGeneratedCode = true;
                }

                if (!holdsGeneratedCode) continue;

                AssetDatabase.DeleteAsset(asmrefPath);
                removed.Add(folder);
            }

            if (removed.Count == 0) return;

            Debug.LogWarning("[SoundService] 유사 enum 상수는 프로젝트에 한 벌만 존재할 수 있어 " +
                             $"이전 생성 폴더를 정리했습니다: {string.Join(", ", removed)}");
        }

        internal static void ClearCache()
        {
            _cachedSettings = null;
            _ambiguityHintShown = false;
        }

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
