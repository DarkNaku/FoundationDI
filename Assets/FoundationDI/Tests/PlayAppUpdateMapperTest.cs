using DarkNaku.FoundationDI;
using NUnit.Framework;

public class PlayAppUpdateMapperTest
{
    // com.google.android.play.core.install.model.UpdateAvailability
    private const int UNKNOWN = 0;
    private const int UPDATE_NOT_AVAILABLE = 1;
    private const int UPDATE_AVAILABLE = 2;
    private const int DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS = 3;

    [Test]
    public void 업데이트_가능과_진행_중은_Available_없음은_UpToDate_그_밖은_Unknown이다()
    {
        Assert.AreEqual(AppUpdateStatus.Available, PlayAppUpdateMapper.ToResult(UPDATE_AVAILABLE, null, 0).Status);
        Assert.AreEqual(AppUpdateStatus.Available,
            PlayAppUpdateMapper.ToResult(DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS, null, 0).Status);
        Assert.AreEqual(AppUpdateStatus.UpToDate, PlayAppUpdateMapper.ToResult(UPDATE_NOT_AVAILABLE, null, 0).Status);
        Assert.AreEqual(AppUpdateStatus.Unknown, PlayAppUpdateMapper.ToResult(UNKNOWN, null, 0).Status);
        Assert.AreEqual(AppUpdateStatus.Unknown, PlayAppUpdateMapper.ToResult(99, null, 0).Status);
    }

    [Test]
    public void 경과_일수가_없으면_null이고_우선순위는_업데이트_가능일_때만_싣는다()
    {
        var available = PlayAppUpdateMapper.ToResult(UPDATE_AVAILABLE, 5, 4);
        var unknownStaleness = PlayAppUpdateMapper.ToResult(UPDATE_AVAILABLE, null, 4);
        // 우선순위는 UPDATE_AVAILABLE일 때만 정의된 값이다(AppUpdateInfo.updatePriority 문서).
        var inProgress = PlayAppUpdateMapper.ToResult(DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS, 5, 4);

        Assert.AreEqual(5, available.DaysSinceRelease);
        Assert.AreEqual(4, available.Priority);
        Assert.IsNull(unknownStaleness.DaysSinceRelease);
        Assert.AreEqual(5, inProgress.DaysSinceRelease);
        Assert.IsNull(inProgress.Priority);
    }

    [Test]
    public void 스토어_페이지는_market_URL이다()
    {
        Assert.AreEqual("market://details?id=com.darknaku.game", PlayAppUpdateMapper.StoreUrl("com.darknaku.game"));
    }
}
