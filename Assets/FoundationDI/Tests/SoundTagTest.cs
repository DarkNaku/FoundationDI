using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class SoundTagTest
{
    [Test]
    public void 기본값과_Null은_모두_null_태그로_취급된다()
    {
        Assert.IsTrue(default(SFX).IsNull);
        Assert.IsTrue(SFX.Null.IsNull);
        Assert.IsTrue(default(Track).IsNull);
        Assert.IsTrue(default(Output).IsNull);
    }

    [Test]
    public void 태그로_만든_값은_문자열로_변환된다()
    {
        var sfx = SFX.FromTag("Jump");

        Assert.AreEqual("Jump", sfx.ToString());
        Assert.AreEqual("Jump", (string)sfx);
        Assert.IsFalse(sfx.IsNull);
    }

    [Test]
    public void 같은_태그끼리는_동등하다()
    {
        Assert.AreEqual(SFX.FromTag("Jump"), SFX.FromTag("Jump"));
        Assert.IsTrue(SFX.FromTag("Jump") == SFX.FromTag("Jump"));
        Assert.IsTrue(SFX.FromTag("Jump") != SFX.FromTag("Land"));
    }

    [Test]
    public void 빈_태그는_Null과_같다()
    {
        Assert.AreEqual(SFX.Null, SFX.FromTag(""));
        Assert.AreEqual(SFX.Null, SFX.FromTag(null));
    }
}

public class ShuffleExtensionsTest
{
    [Test]
    public void 섞어도_원소의_구성은_유지된다()
    {
        var queue = new Queue<int>(new[] { 1, 2, 3, 4, 5 });

        queue.Shuffle();

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, queue);
    }

    [Test]
    public void 빈_큐를_섞어도_예외가_없다()
    {
        var queue = new Queue<int>();

        Assert.DoesNotThrow(() => queue.Shuffle());
        Assert.AreEqual(0, queue.Count);
    }
}

public class SoundServiceSettingsTest
{
    [Test]
    public void 데이터_경로는_Assets로_시작하고_슬래시로_끝나게_정규화된다()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<SoundServiceSettings>();

        settings.DataRootPath = "MyData\\Sound";

        Assert.AreEqual("Assets/MyData/Sound/", settings.GetNormalizedDataRootPath());

        UnityEngine.Object.DestroyImmediate(settings);
    }

    [Test]
    public void 데이터_경로가_비어있으면_기본_경로를_쓴다()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<SoundServiceSettings>();

        settings.DataRootPath = "   ";

        Assert.AreEqual("Assets/FoundationDI.Data/SoundService/", settings.GetNormalizedDataRootPath());

        UnityEngine.Object.DestroyImmediate(settings);
    }
}
