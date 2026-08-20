using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundDataTest
{
    private static AudioClip MakeClip(string name) => AudioClip.Create(name, 1, 1, 1000, false);

    [Test]
    public void 인덱스로_지정한_클립을_반환한다()
    {
        var first = MakeClip("first");
        var second = MakeClip("second");
        var data = new SoundData("Jump", new[] { first, second }, CompressionPreset.FrequentSound, false);

        Assert.AreSame(first, data.GetClip(0));
        Assert.AreSame(second, data.GetClip(1));
    }

    [Test]
    public void 인덱스가_음수면_등록된_클립_중_하나를_반환한다()
    {
        var first = MakeClip("first");
        var second = MakeClip("second");
        var data = new SoundData("Jump", new[] { first, second }, CompressionPreset.FrequentSound, false);

        var clip = data.GetClip(-1);

        Assert.That(clip, Is.EqualTo(first).Or.EqualTo(second));
    }

    [Test]
    public void 범위를_벗어난_인덱스는_경고하고_무작위_클립으로_대체한다()
    {
        var only = MakeClip("only");
        var data = new SoundData("Jump", new[] { only }, CompressionPreset.FrequentSound, false);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*인덱스 '5'.*"));

        Assert.AreSame(only, data.GetClip(5));
    }

    [Test]
    public void 클립이_없으면_경고하고_null을_반환한다()
    {
        var data = new SoundData("Empty", new AudioClip[0], CompressionPreset.FrequentSound, false);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*오디오 클립이 없습니다.*"));

        Assert.IsNull(data.GetClip(0));
    }
}
