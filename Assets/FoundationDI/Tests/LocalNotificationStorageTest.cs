using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class LocalNotificationStorageTest
{
    private const string OpenedKey = "FoundationDI.LocalNotification.LastOpened";

    // 실제 PlayerPrefs를 건드리므로 앞뒤로 반드시 청소한다.
    [SetUp]
    public void SetUp() => PlayerPrefs.DeleteKey(OpenedKey);

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey(OpenedKey);

    [Test]
    public void 처음_보는_알림만_연_것으로_기록하고_새_인스턴스에서도_기억한다()
    {
        Assert.IsTrue(new PlayerPrefsLocalNotificationStorage().TryMarkOpened("daily|100"));
        Assert.IsFalse(new PlayerPrefsLocalNotificationStorage().TryMarkOpened("daily|100"));
        Assert.IsTrue(new PlayerPrefsLocalNotificationStorage().TryMarkOpened("daily|200"));
    }

    [Test]
    public void 데이터가_없는_알림은_기록하지_않는다()
    {
        var sut = new PlayerPrefsLocalNotificationStorage();

        Assert.IsFalse(sut.TryMarkOpened(null));
        Assert.IsFalse(sut.TryMarkOpened(""));
    }
}
