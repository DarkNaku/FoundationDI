using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundCatalogTest
{
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
    public void Keys는_등록_순서대로_노출된다()
    {
        var catalog = MakeCatalogWithClips(("A", MakeClip(), false), ("B", MakeClip(), false));

        CollectionAssert.AreEqual(new[] { "A", "B" }, (List<string>)((ISoundCatalog)catalog).Keys);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 중복_키는_경고를_남기고_마지막_값을_채택한다()
    {
        var last = MakeClip();
        var catalog = MakeCatalogWithClips(("X", MakeClip(), false), ("X", last, false));

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate key"));

        var found = ((ISoundCatalog)catalog).TryGetClip("X", out var clip);

        Assert.IsTrue(found);
        Assert.AreSame(last, clip);

        Object.DestroyImmediate(catalog);
    }
}
