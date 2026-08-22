using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // 설정 SO의 상품 목록으로 IapProducts 상수 클래스를 생성한다.
    // 문자열 리터럴로 상품을 부르면 오타가 런타임까지 살아남는다 — 컴파일 타임에 잡히게 한다.
    public static class IapProductConstantsGenerator
    {
        private const string RuntimeAssemblyName = "FoundationDI";
        private const string GeneratedFileName = "IapProducts.cs";
        private const string GeneratedFolderName = "Generated";

        [MenuItem("Tools/FoundationDI/IAP/Generate Product Constants")]
        public static void Generate()
        {
            var settings = FindSettings();

            if (settings == null)
            {
                Debug.LogError("[IAPService] IapServiceSettings 에셋을 찾지 못했다. " +
                               "Create > FoundationDI > IAP Service Settings로 먼저 만들 것.");
                return;
            }

            var settingsPath = AssetDatabase.GetAssetPath(settings);
            var folder = settingsPath[..(settingsPath.LastIndexOf('/') + 1)] + GeneratedFolderName + "/";

            EnsureFolder(folder);
            EnsureAssemblyReference(folder);

            var filePath = folder + GeneratedFileName;
            File.WriteAllText(filePath, BuildSource(settings.Products));

            AssetDatabase.ImportAsset(filePath);
            AssetDatabase.Refresh();

            Debug.Log($"[IAPService] 상품 상수를 생성했다: {filePath}");
        }

        internal static string BuildSource(IReadOnlyList<IapProductEntry> entries)
        {
            const string indent = "    ";

            var builder = new StringBuilder();
            builder.AppendLine("// 이 파일은 IAPService 에디터 도구가 자동 생성합니다. 직접 수정하지 마세요.");
            builder.AppendLine("namespace DarkNaku.FoundationDI");
            builder.AppendLine("{");
            builder.AppendLine(indent + "public static class IapProducts");
            builder.AppendLine(indent + "{");

            if (entries != null)
            {
                var used = new HashSet<string>();

                foreach (var entry in entries)
                {
                    if (entry == null) continue;

                    var identifier = ToIdentifier(entry.Id);

                    if (identifier == null)
                    {
                        Debug.LogWarning($"[IAPService] 식별자로 바꿀 수 없는 상품 ID를 건너뛴다: {entry.Id}");
                        continue;
                    }

                    // 서로 다른 ID가 같은 식별자로 접히면 컴파일이 깨진다. 먼저 온 쪽을 남긴다.
                    if (!used.Add(identifier))
                    {
                        Debug.LogWarning($"[IAPService] 식별자가 충돌해 건너뛴다: {entry.Id} → {identifier}");
                        continue;
                    }

                    builder.AppendLine($"{indent}{indent}public const string {identifier} = \"{entry.Id}\";");
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

        private static IapServiceSettings FindSettings()
        {
            if (Selection.activeObject is IapServiceSettings selected) return selected;

            var guids = AssetDatabase.FindAssets("t:IapServiceSettings");

            if (guids.Length == 0) return null;

            if (guids.Length > 1)
            {
                Debug.LogWarning("[IAPService] IapServiceSettings가 여러 개다. 첫 번째를 쓴다. " +
                                 "특정 에셋을 쓰려면 프로젝트 창에서 선택한 뒤 다시 실행할 것.");
            }

            return AssetDatabase.LoadAssetAtPath<IapServiceSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder.TrimEnd('/'))) return;

            var trimmed = folder.TrimEnd('/');
            var parent = trimmed[..trimmed.LastIndexOf('/')];
            var name = trimmed[(trimmed.LastIndexOf('/') + 1)..];

            AssetDatabase.CreateFolder(parent, name);
        }

        // 생성 코드가 게임 프로젝트의 어느 어셈블리에 들어갈지 확정되지 않으면
        // 자기 asmdef를 쓰는 프로젝트에서 참조가 끊긴다. asmref로 런타임 어셈블리에 합류시킨다.
        private static void EnsureAssemblyReference(string folder)
        {
            var asmrefPath = folder + RuntimeAssemblyName + ".asmref";

            if (File.Exists(asmrefPath)) return;

            File.WriteAllText(asmrefPath, "{\n    \"reference\": \"" + RuntimeAssemblyName + "\"\n}");
            AssetDatabase.ImportAsset(asmrefPath);
        }
    }
}
