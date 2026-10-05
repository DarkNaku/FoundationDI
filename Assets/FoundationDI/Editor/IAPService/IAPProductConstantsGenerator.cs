using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Purchasing;

namespace DarkNaku.FoundationDI.Editor
{
    // Unity IAP Catalog의 상품으로 IAPProducts 상수 클래스를 생성한다.
    // 문자열 리터럴로 상품을 부르면 오타가 런타임까지 살아남는다 — 컴파일 타임에 잡히게 한다.
    public static class IAPProductConstantsGenerator
    {
        private const string RuntimeAssemblyName = "FoundationDI";
        private const string GeneratedFileName = "IAPProducts.cs";
        private const string GeneratedHeader = "// 이 파일은 IAPService 에디터 도구가 자동 생성합니다. 직접 수정하지 마세요.";

        // EditorPrefs는 기기 단위라 프로젝트를 구분하지 않는다 — 프로젝트 GUID를 키에 붙인다.
        private static string FolderPrefsKey => "FoundationDI.IAP.ConstantsFolder." + PlayerSettings.productGUID;

        [MenuItem("Tools/FoundationDI/IAP/Generate Product Constants")]
        public static void Generate()
        {
            var catalog = ProductCatalog.LoadDefaultCatalog();

            UnityIAPCatalog.WarnIfCodelessAutoInitialization(catalog);

            if (catalog.IsEmpty())
            {
                Debug.LogError("[IAPService] IAP Catalog가 비어 있다. " +
                               "Services > In-App Purchasing > IAP Catalog에서 상품을 먼저 추가할 것.");
                return;
            }

            var folder = ResolveOutputFolder(EditorPrefs.GetString(FolderPrefsKey, null), FindExistingConstants())
                         ?? AskOutputFolder();

            if (folder == null) return;

            EditorPrefs.SetString(FolderPrefsKey, folder);

            EnsureFolder(folder);
            EnsureAssemblyReference(folder);

            var ids = new List<string>();
            foreach (var item in catalog.allValidProducts) ids.Add(item.id);

            var filePath = folder + GeneratedFileName;
            File.WriteAllText(filePath, BuildSource(ids));

            AssetDatabase.ImportAsset(filePath);
            AssetDatabase.Refresh();

            Debug.Log($"[IAPService] 상품 상수를 생성했다: {filePath}");
        }

        // 기억한 폴더 → 이미 생성된 파일의 폴더 → (null이면) 묻는다.
        // 기존 파일을 우선하는 이유: 0.10.x는 설정 SO 옆에 만들었다. 그 자리를 덮어 써야 게임 코드의
        // 참조와 asmref가 그대로 유지된다.
        internal static string ResolveOutputFolder(string rememberedFolder, string existingConstantsPath)
        {
            if (IsAssetsFolder(rememberedFolder)) return Normalize(rememberedFolder);

            if (!string.IsNullOrEmpty(existingConstantsPath))
            {
                var folder = existingConstantsPath[..(existingConstantsPath.LastIndexOf('/') + 1)];
                if (IsAssetsFolder(folder)) return folder;
            }

            return null;
        }

        internal static string BuildSource(IEnumerable<string> productIds)
        {
            const string indent = "    ";

            var builder = new StringBuilder();
            builder.AppendLine(GeneratedHeader);
            builder.AppendLine("namespace DarkNaku.FoundationDI");
            builder.AppendLine("{");
            builder.AppendLine(indent + "public static class IAPProducts");
            builder.AppendLine(indent + "{");

            if (productIds != null)
            {
                var used = new HashSet<string>();

                foreach (var id in productIds)
                {
                    var identifier = ToIdentifier(id);

                    if (identifier == null)
                    {
                        Debug.LogWarning($"[IAPService] 식별자로 바꿀 수 없는 상품 ID를 건너뛴다: {id}");
                        continue;
                    }

                    // 서로 다른 ID가 같은 식별자로 접히면 컴파일이 깨진다. 먼저 온 쪽을 남긴다.
                    if (!used.Add(identifier))
                    {
                        Debug.LogWarning($"[IAPService] 식별자가 충돌해 건너뛴다: {id} → {identifier}");
                        continue;
                    }

                    builder.AppendLine($"{indent}{indent}public const string {identifier} = \"{id}\";");
                }
            }

            builder.AppendLine(indent + "}");
            builder.AppendLine("}");

            return builder.ToString();
        }

        // remove_ads → RemoveAds, gem.pack-100 → GemPack100, 100_gems → _100Gems
        internal static string ToIdentifier(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return null;

            var builder = new StringBuilder(productId.Length);
            var upperNext = true;

            foreach (var c in productId)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                    continue;
                }

                // 구분자(_ . - 공백 등)는 버리고 다음 글자를 대문자로 만든다.
                upperNext = true;
            }

            if (builder.Length == 0) return null;

            // C# 식별자는 숫자로 시작할 수 없다.
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');

            return builder.ToString();
        }

        private static bool IsAssetsFolder(string folder) =>
            !string.IsNullOrEmpty(folder) && (folder == "Assets" || folder.StartsWith("Assets/"));

        private static string Normalize(string folder) => folder.EndsWith("/") ? folder : folder + "/";

        // 우리 도구가 만든 파일만 찾는다 — 게임이 직접 만든 같은 이름의 파일을 덮어 쓰지 않게.
        private static string FindExistingConstants()
        {
            foreach (var guid in AssetDatabase.FindAssets("IAPProducts t:MonoScript", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + GeneratedFileName)) continue;

                using var lines = File.ReadLines(path).GetEnumerator();
                if (lines.MoveNext() && lines.Current == GeneratedHeader) return path;
            }

            return null;
        }

        private static string AskOutputFolder()
        {
            var absolute = EditorUtility.OpenFolderPanel("IAPProducts.cs를 생성할 폴더", Application.dataPath, string.Empty);

            if (string.IsNullOrEmpty(absolute)) return null;

            var projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') + "/";
            var relative = absolute.Replace('\\', '/');

            if (relative.StartsWith(projectRoot)) relative = relative[projectRoot.Length..];

            if (!IsAssetsFolder(relative))
            {
                Debug.LogError($"[IAPService] Assets/ 아래 폴더를 골라야 한다: {absolute}");
                return null;
            }

            return Normalize(relative);
        }

        private static void EnsureFolder(string folder)
        {
            var trimmed = folder.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(trimmed)) return;

            var parent = trimmed[..trimmed.LastIndexOf('/')];
            EnsureFolder(parent + "/");

            AssetDatabase.CreateFolder(parent, trimmed[(trimmed.LastIndexOf('/') + 1)..]);
        }

        // 생성 코드가 게임 프로젝트의 어느 어셈블리에 들어갈지 확정되지 않으면
        // 자기 asmdef를 쓰는 프로젝트에서 참조가 끊긴다. asmref로 런타임 어셈블리에 합류시킨다.
        // 상수는 문자열뿐이라 Unity IAP가 없는 코어 어셈블리에 들어가도 컴파일된다.
        private static void EnsureAssemblyReference(string folder)
        {
            var asmrefPath = folder + RuntimeAssemblyName + ".asmref";

            if (File.Exists(asmrefPath)) return;

            File.WriteAllText(asmrefPath, "{\n    \"reference\": \"" + RuntimeAssemblyName + "\"\n}");
            AssetDatabase.ImportAsset(asmrefPath);
        }
    }
}
