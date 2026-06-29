using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
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
