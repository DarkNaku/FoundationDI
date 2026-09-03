using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

// 런타임 스트리핑 자체는 EditMode에서 재현할 수 없다. 대신 재발할 실패 모드를 막는다 —
// "어댑터를 추가하고 보존 표에 넣는 것을 잊는다". 그 누락은 에디터에서 아무 증상도 내지 않고
// IL2CPP 실기 빌드에서만 드러나므로, asmdef를 사실의 원본으로 삼아 표와 대조한다.
public class FoundationDILinkXmlTest
{
    [Serializable]
    private class AsmdefJson
    {
        public string name;
        public string[] references;
        public string[] precompiledReferences;
        public string[] defineConstraints;
    }

    private class AdapterAsmdef
    {
        public string Name;
        public string Directory;
        public List<string> ThirdPartyReferences;
    }

    // defineConstraints가 걸린 FoundationDI.* 런타임 asmdef = 옵셔널 어댑터 어셈블리.
    // 심볼이 없으면 존재조차 하지 않고, 코어가 참조할 수 없어 링커에게는 섬으로 보이는 것들이다.
    private static List<AdapterAsmdef> CollectAdapterAsmdefs()
    {
        var adapters = new List<AdapterAsmdef>();

        foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("/Tests/")) continue;

            var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);

            if (asset == null) continue;

            var json = JsonUtility.FromJson<AsmdefJson>(asset.text);

            if (json == null || string.IsNullOrEmpty(json.name)) continue;
            if (!json.name.StartsWith("FoundationDI.")) continue;
            if (json.defineConstraints == null || json.defineConstraints.Length == 0) continue;

            var thirdParty = new List<string>();

            if (json.references != null)
            {
                thirdParty.AddRange(json.references
                    .Select(ResolveReference)
                    .Where(r => !string.IsNullOrEmpty(r) && r != "FoundationDI"));
            }

            // overrideReferences를 켠 어댑터(Firebase)는 서드파티가 references가 아니라
            // precompiledReferences에 DLL 이름으로 들어 있다. 링커는 확장자 없는 어셈블리
            // 이름을 쓰므로 여기서 벗겨 맞춘다.
            if (json.precompiledReferences != null)
            {
                thirdParty.AddRange(json.precompiledReferences
                    .Select(r => r.EndsWith(".dll") ? r.Substring(0, r.Length - 4) : r));
            }

            adapters.Add(new AdapterAsmdef
            {
                Name = json.name,
                Directory = Path.GetDirectoryName(path).Replace('\\', '/'),
                ThirdPartyReferences = thirdParty,
            });
        }

        return adapters;
    }

    // asmdef가 "Use GUIDs"로 저장되면 참조가 "GUID:..." 꼴이 된다. 이름으로 되돌려 놓지 않으면
    // 표와 대조할 수 없어 엉뚱한 실패가 난다.
    private static string ResolveReference(string reference)
    {
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith("GUID:")) return reference;

        var path = AssetDatabase.GUIDToAssetPath(reference.Substring("GUID:".Length));
        var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);

        return asset == null ? reference : JsonUtility.FromJson<AsmdefJson>(asset.text).name;
    }

    // 에셋 경로는 프로젝트 루트 기준이지만, UPM으로 설치된 패키지는 실제로 Library/PackageCache
    // 아래에 있어 그대로는 파일 IO가 되지 않는다.
    private static string ToFileSystemPath(string assetPath)
    {
        if (!assetPath.StartsWith("Packages/")) return assetPath;

        var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);

        if (info == null) return assetPath;

        var rest = assetPath.Substring(("Packages/" + info.name).Length).TrimStart('/');

        return string.IsNullOrEmpty(rest) ? info.resolvedPath : Path.Combine(info.resolvedPath, rest);
    }

    [Test]
    public void 어댑터_어셈블리는_모두_보존표에_있다()
    {
        var declared = SdkDefineTable.Entries.Select(e => e.AdapterAssembly).ToList();
        var actual = CollectAdapterAsmdefs().Select(a => a.Name).ToList();

        CollectionAssert.IsNotEmpty(actual, "어댑터 asmdef를 하나도 못 찾았다. 수집 조건이 깨졌다.");
        CollectionAssert.AreEquivalent(actual, declared,
            "SdkDefineTable의 AdapterAssembly 목록이 실제 어댑터 asmdef와 다르다. " +
            "어댑터를 추가했다면 표에도 한 줄 넣어라 — 안 넣으면 IL2CPP 빌드에서만 조용히 사라진다.");
    }

    [Test]
    public void 어댑터가_참조하는_SDK_어셈블리는_모두_보존표에_있다()
    {
        var table = SdkDefineTable.Entries.ToDictionary(e => e.AdapterAssembly);

        foreach (var adapter in CollectAdapterAsmdefs())
        {
            Assert.IsTrue(table.ContainsKey(adapter.Name), $"{adapter.Name}이 표에 없다.");

            var preserved = table[adapter.Name].PreservedAssemblies;

            foreach (var reference in adapter.ThirdPartyReferences)
            {
                CollectionAssert.Contains(preserved, reference,
                    $"{adapter.Name}이 참조하는 {reference}가 PreservedAssemblies에 없다. " +
                    "어댑터만 보존하면 부족하다 — SDK는 네이티브가 이름으로 되부르는 경로가 있어 " +
                    "링커가 그 사용을 볼 수 없다(이번에 MaxSdk가 통째로 사라진 이유).");
            }
        }
    }

    [Test]
    public void 어댑터_폴더마다_AlwaysLinkAssembly가_있다()
    {
        foreach (var adapter in CollectAdapterAsmdefs())
        {
            var directory = ToFileSystemPath(adapter.Directory);

            Assert.IsTrue(Directory.Exists(directory), $"{adapter.Name} 폴더를 찾지 못했다: {directory}");

            var hasAttribute = Directory.GetFiles(directory, "*.cs")
                .Any(file => File.ReadAllText(file).Contains("[assembly: AlwaysLinkAssembly]"));

            Assert.IsTrue(hasAttribute,
                $"{adapter.Name} 폴더에 [assembly: AlwaysLinkAssembly]가 없다. " +
                "link.xml이 닿지 않는 빌드 경로에서 어댑터를 살려 두는 2차 방어선이다.");
        }
    }

    [Test]
    public void 생성된_link_xml은_표의_모든_어셈블리를_보존한다()
    {
        var xml = FoundationDILinkXmlGenerator.BuildLinkXml(SdkDefineTable.Entries);

        foreach (var entry in SdkDefineTable.Entries)
        {
            StringAssert.Contains($"fullname=\"{entry.AdapterAssembly}\"", xml);

            foreach (var assembly in entry.PreservedAssemblies)
            {
                StringAssert.Contains($"fullname=\"{assembly}\"", xml);
            }
        }
    }

    [Test]
    public void 모든_항목에_ignoreIfMissing이_붙는다()
    {
        var xml = FoundationDILinkXmlGenerator.BuildLinkXml(SdkDefineTable.Entries);

        var all = Regex.Matches(xml, "<assembly ").Count;
        var guarded = Regex.Matches(xml, "ignoreIfMissing=\"1\"").Count;

        Assert.Greater(all, 0);

        // 어댑터는 심볼이 없으면 아예 존재하지 않고 SDK도 프로젝트마다 없을 수 있다.
        // 하나라도 빠지면 그 SDK를 안 쓰는 프로젝트에서 링커가 에러를 낸다.
        Assert.AreEqual(all, guarded, "ignoreIfMissing이 없는 항목이 있다.");
    }

    [Test]
    public void 같은_어셈블리를_두_번_쓰지_않는다()
    {
        var xml = FoundationDILinkXmlGenerator.BuildLinkXml(SdkDefineTable.Entries);

        var names = Regex.Matches(xml, "fullname=\"([^\"]+)\"")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();

        CollectionAssert.AllItemsAreUnique(names);
    }

    [Test]
    public void 링커에_넘길_파일을_Assets_밖에_쓰고_절대경로를_돌려준다()
    {
        var path = new FoundationDILinkXmlGenerator().GenerateAdditionalLinkXmlFile(null, null);

        Assert.IsTrue(Path.IsPathRooted(path), $"절대경로가 아니다: {path}");
        Assert.IsTrue(File.Exists(path), $"파일이 없다: {path}");
        StringAssert.Contains("<linker>", File.ReadAllText(path));

        // Assets/ 안에 쓰면 빌드마다 에셋 임포트가 돌고 소비 프로젝트 작업 트리를 더럽힌다.
        Assert.IsFalse(path.Replace('\\', '/').Contains("/Assets/"), path);
    }
}
