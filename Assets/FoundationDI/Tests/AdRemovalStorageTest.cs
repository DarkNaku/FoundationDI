using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class AdRemovalStorageTest
{
    private const string Key = "FOUNDATIONDI_ADS_REMOVED";

    // 실제 PlayerPrefs를 건드리므로 앞뒤로 반드시 청소한다.
    // 청소하지 않으면 개발자의 에디터 설정에 광고제거 플래그가 남는다.
    [SetUp]
    public void SetUp() => PlayerPrefs.DeleteKey(Key);

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey(Key);

    [Test]
    public void 저장된_값이_없으면_광고제거는_거짓이다()
    {
        var sut = new PlayerPrefsAdRemovalStorage();

        Assert.IsFalse(sut.Load());
    }

    [Test]
    public void 저장한_광고제거_상태가_새_인스턴스에서_복원된다()
    {
        new PlayerPrefsAdRemovalStorage().Save(true);

        Assert.IsTrue(new PlayerPrefsAdRemovalStorage().Load());

        new PlayerPrefsAdRemovalStorage().Save(false);

        Assert.IsFalse(new PlayerPrefsAdRemovalStorage().Load());
    }
}
