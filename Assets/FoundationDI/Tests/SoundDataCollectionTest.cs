using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundDataCollectionTest
{
    private SoundDataCollection _collection;

    private static AudioClip MakeClip(string name) => AudioClip.Create(name, 1, 1, 1000, false);

    [SetUp]
    public void SetUp()
    {
        _collection = ScriptableObject.CreateInstance<SoundDataCollection>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_collection);
    }

    [Test]
    public void 새_태그로_사운드를_만들면_조회할_수_있다()
    {
        bool created = _collection.CreateSound(new[] { MakeClip("a") }, "Jump",
            CompressionPreset.FrequentSound, false, out string message);

        Assert.IsTrue(created, message);
        Assert.IsNotNull(_collection.GetSound("Jump"));
        Assert.AreEqual(1, _collection.Sounds.Length);
    }

    [Test]
    public void 중복_태그로는_사운드를_만들_수_없다()
    {
        _collection.CreateSound(new[] { MakeClip("a") }, "Jump", CompressionPreset.FrequentSound, false, out _);

        bool created = _collection.CreateSound(new[] { MakeClip("b") }, "Jump",
            CompressionPreset.FrequentSound, false, out string message);

        Assert.IsFalse(created);
        Assert.That(message, Does.Contain("Jump"));
        Assert.AreEqual(1, _collection.Sounds.Length);
    }

    [Test]
    public void 클립이_없으면_사운드를_만들_수_없다()
    {
        bool created = _collection.CreateSound(new AudioClip[0], "Jump",
            CompressionPreset.FrequentSound, false, out _);

        Assert.IsFalse(created);
    }

    [Test]
    public void 태그가_비어있으면_사운드를_만들_수_없다()
    {
        bool created = _collection.CreateSound(new[] { MakeClip("a") }, string.Empty,
            CompressionPreset.FrequentSound, false, out _);

        Assert.IsFalse(created);
    }

    [Test]
    public void 등록되지_않은_태그는_경고하고_null을_반환한다()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Missing.*"));

        Assert.IsNull(_collection.GetSound("Missing"));
    }

    [Test]
    public void 태그를_바꾸면_새_태그로_조회된다()
    {
        _collection.CreateSound(new[] { MakeClip("a") }, "Jump", CompressionPreset.FrequentSound, false, out _);

        bool edited = _collection.EditSound("Jump", "Leap", new[] { MakeClip("b") }, out string message);

        Assert.IsTrue(edited, message);
        Assert.IsNotNull(_collection.GetSound("Leap"));
    }

    [Test]
    public void 이미_존재하는_태그로는_수정할_수_없다()
    {
        _collection.CreateSound(new[] { MakeClip("a") }, "Jump", CompressionPreset.FrequentSound, false, out _);
        _collection.CreateSound(new[] { MakeClip("b") }, "Land", CompressionPreset.FrequentSound, false, out _);

        bool edited = _collection.EditSound("Jump", "Land", new[] { MakeClip("c") }, out _);

        Assert.IsFalse(edited);
    }

    [Test]
    public void 사운드를_제거하면_목록에서_사라진다()
    {
        _collection.CreateSound(new[] { MakeClip("a") }, "Jump", CompressionPreset.FrequentSound, false, out _);

        _collection.RemoveSound("Jump");

        Assert.AreEqual(0, _collection.Sounds.Length);
    }
}
