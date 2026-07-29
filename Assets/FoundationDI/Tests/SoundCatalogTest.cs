using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundCatalogTest
{
    private static SoundCatalogSO MakeCatalog(string json)
    {
        var catalog = ScriptableObject.CreateInstance<SoundCatalogSO>();
        JsonUtility.FromJsonOverwrite(json, catalog);
        return catalog;
    }

    private static AudioClip MakeClip() => AudioClip.Create("clip", 1, 1, 1000, false);

    private static SoundCatalogSO MakeCatalogWithClips(params (string key, AudioClip clip, bool preload)[] entries)
    {
        var catalog = ScriptableObject.CreateInstance<SoundCatalogSO>();
        var so = new SerializedObject(catalog);
        var list = so.FindProperty("_entries");
        list.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("Key").stringValue = entries[i].key;
            e.FindPropertyRelative("Clip").objectReferenceValue = entries[i].clip;
            e.FindPropertyRelative("Preload").boolValue = entries[i].preload;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return catalog;
    }

    [Test]
    public void 등록된_키는_클립으로_변환된다()
    {
        var clip = MakeClip();
        var catalog = MakeCatalogWithClips(("Jump", clip, false));

        var found = ((ISoundCatalog)catalog).TryGetClip("Jump", out var result);

        Assert.IsTrue(found);
        Assert.AreSame(clip, result);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 미등록_키는_클립_변환에_실패한다()
    {
        var catalog = MakeCatalogWithClips();

        var found = ((ISoundCatalog)catalog).TryGetClip("None", out var clip);

        Assert.IsFalse(found);
        Assert.IsNull(clip);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void PreloadClips는_Preload가_true인_클립만_노출한다()
    {
        var a = MakeClip();
        var b = MakeClip();
        var c = MakeClip();
        var catalog = MakeCatalogWithClips(("A", a, true), ("B", b, false), ("C", c, true));

        CollectionAssert.AreEquivalent(
            new[] { a, c },
            new List<AudioClip>(((ISoundCatalog)catalog).PreloadClips));

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 등록된_키는_리소스키로_변환된다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"Jump\",\"ResourceKey\":\"sfx/jump\",\"Preload\":false}]}");

        var found = ((ISoundCatalog)catalog).TryGetResourceKey("Jump", out var resourceKey);

        Assert.IsTrue(found);
        Assert.AreEqual("sfx/jump", resourceKey);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 미등록_키는_변환에_실패한다()
    {
        var catalog = MakeCatalog("{\"_entries\":[]}");

        var found = ((ISoundCatalog)catalog).TryGetResourceKey("None", out _);

        Assert.IsFalse(found);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void Keys는_등록_순서대로_노출된다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"A\",\"ResourceKey\":\"r/a\",\"Preload\":false}," +
            "{\"Key\":\"B\",\"ResourceKey\":\"r/b\",\"Preload\":false}]}");

        CollectionAssert.AreEqual(new[] { "A", "B" }, (List<string>)((ISoundCatalog)catalog).Keys);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void PreloadResourceKeys는_Preload가_true인_항목만_노출한다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"A\",\"ResourceKey\":\"r/a\",\"Preload\":true}," +
            "{\"Key\":\"B\",\"ResourceKey\":\"r/b\",\"Preload\":false}," +
            "{\"Key\":\"C\",\"ResourceKey\":\"r/c\",\"Preload\":true}]}");

        CollectionAssert.AreEquivalent(
            new[] { "r/a", "r/c" },
            new List<string>(((ISoundCatalog)catalog).PreloadResourceKeys));

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 중복_키는_경고를_남기고_마지막_값을_채택한다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"X\",\"ResourceKey\":\"r/x1\",\"Preload\":false}," +
            "{\"Key\":\"X\",\"ResourceKey\":\"r/x2\",\"Preload\":false}]}");

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate key"));

        var found = ((ISoundCatalog)catalog).TryGetResourceKey("X", out var resourceKey);

        Assert.IsTrue(found);
        Assert.AreEqual("r/x2", resourceKey);

        Object.DestroyImmediate(catalog);
    }
}
