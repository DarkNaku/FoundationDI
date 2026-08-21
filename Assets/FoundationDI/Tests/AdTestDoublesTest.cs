using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdTestDoublesTest
{
    [Test]
    public void 가짜_디스패처는_지정_시간이_지나야_지연작업을_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.Delay(5f, () => ran++);

        dispatcher.Advance(4.9f);
        Assert.AreEqual(0, ran, "아직 시간이 안 됐는데 실행됐다");

        dispatcher.Advance(0.2f);
        Assert.AreEqual(1, ran, "시간이 지났는데 실행되지 않았다");

        dispatcher.Advance(100f);
        Assert.AreEqual(1, ran, "한 번 실행된 작업이 다시 실행됐다");
    }

    [Test]
    public void 가짜_디스패처는_취소된_지연작업을_실행하지_않는다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        var handle = dispatcher.Delay(5f, () => ran++);
        handle.Dispose();

        dispatcher.Advance(10f);

        Assert.AreEqual(0, ran);
    }

    [Test]
    public void 가짜_디스패처는_지정_프레임수가_지나야_작업을_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.NextFrames(2, () => ran++);

        dispatcher.TickFrames(1);
        Assert.AreEqual(0, ran);

        dispatcher.TickFrames(1);
        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 가짜_디스패처는_프레임수가_0이면_즉시_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.NextFrames(0, () => ran++);

        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 가짜_디스패처는_실행중에_예약된_작업을_같은_틱에_실행하지_않는다()
    {
        // 자동 재로드가 재시도를 예약하는 상황을 재현한다.
        // 스냅샷 순회가 깨지면 여기서 무한 루프나 조기 실행이 잡힌다.
        var dispatcher = new FakeAdDispatcher();
        var outer = 0;
        var inner = 0;

        dispatcher.Delay(1f, () =>
        {
            outer++;
            dispatcher.Delay(1f, () => inner++);
        });

        dispatcher.Advance(1f);
        Assert.AreEqual(1, outer);
        Assert.AreEqual(0, inner, "중첩 예약이 같은 틱에 실행됐다");

        dispatcher.Advance(1f);
        Assert.AreEqual(1, inner);
    }

    [Test]
    public void 가짜_디스패처는_PendingCount로_예약되었지만_아직_실행되지_않은_작업수를_센다()
    {
        var dispatcher = new FakeAdDispatcher();

        dispatcher.Delay(5f, () => { });
        dispatcher.NextFrames(3, () => { });

        Assert.AreEqual(2, dispatcher.PendingCount);

        dispatcher.Advance(5f);

        Assert.AreEqual(1, dispatcher.PendingCount);
    }

    [Test]
    public void 가짜_디스패처는_취소된_작업을_PendingCount에서_제외한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var handle = dispatcher.Delay(5f, () => { });

        handle.Dispose();

        Assert.AreEqual(0, dispatcher.PendingCount);
    }

    [Test]
    public void 가짜_디스패처는_모든_작업이_실행되면_PendingCount가_0이다()
    {
        var dispatcher = new FakeAdDispatcher();
        dispatcher.Delay(1f, () => { });
        dispatcher.NextFrames(1, () => { });

        dispatcher.Advance(1f);
        dispatcher.TickFrames(1);

        Assert.AreEqual(0, dispatcher.PendingCount);
    }

    [Test]
    public void 가짜_전면어댑터는_RaiseLoaded_호출시_IsReady를_true로_바꾸고_Loaded_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var loadedCount = 0;
        adapter.Loaded += () => loadedCount++;

        adapter.RaiseLoaded();

        Assert.IsTrue(adapter.IsReady);
        Assert.AreEqual(1, loadedCount);
    }

    [Test]
    public void 가짜_전면어댑터는_RaiseLoadFailed_호출시_IsReady를_false로_바꾸고_LoadFailed_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = true };
        AdError? received = null;
        adapter.LoadFailed += e => received = e;
        var error = new AdError(1, "load failed");

        adapter.RaiseLoadFailed(error);

        Assert.IsFalse(adapter.IsReady);
        Assert.AreEqual(error.Code, received.Value.Code);
        Assert.AreEqual(error.Message, received.Value.Message);
    }

    [Test]
    public void 가짜_전면어댑터는_RaiseDisplayed_호출시_IsReady를_바꾸지_않고_Displayed_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = true };
        var displayedCount = 0;
        adapter.Displayed += () => displayedCount++;

        adapter.RaiseDisplayed();

        Assert.IsTrue(adapter.IsReady);
        Assert.AreEqual(1, displayedCount);
    }

    [Test]
    public void 가짜_전면어댑터는_RaiseDisplayFailed_호출시_IsReady를_false로_바꾸고_DisplayFailed_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = true };
        AdError? received = null;
        adapter.DisplayFailed += e => received = e;
        var error = new AdError(2, "display failed");

        adapter.RaiseDisplayFailed(error);

        Assert.IsFalse(adapter.IsReady);
        Assert.AreEqual(error.Code, received.Value.Code);
        Assert.AreEqual(error.Message, received.Value.Message);
    }

    [Test]
    public void 가짜_전면어댑터는_RaiseClosed_호출시_IsReady를_false로_바꾸고_Closed_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = true };
        var closedCount = 0;
        adapter.Closed += () => closedCount++;

        adapter.RaiseClosed();

        Assert.IsFalse(adapter.IsReady);
        Assert.AreEqual(1, closedCount);
    }

    [Test]
    public void 가짜_전면어댑터는_RaiseRewarded_호출시_IsReady를_바꾸지_않고_Rewarded_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = true };
        AdReward? received = null;
        adapter.Rewarded += r => received = r;
        var reward = new AdReward("coins", 100);

        adapter.RaiseRewarded(reward);

        Assert.IsTrue(adapter.IsReady);
        Assert.AreEqual(reward.Label, received.Value.Label);
        Assert.AreEqual(reward.Amount, received.Value.Amount);
    }

    [Test]
    public void 가짜_전면어댑터는_RaisePaid_호출시_IsReady를_바꾸지_않고_Paid_이벤트를_발화한다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = true };
        AdImpression? received = null;
        adapter.Paid += p => received = p;
        var impression = new AdImpression(AdFormat.Interstitial, "AdMob", "AdMob", "unit", "placement", "placement",
            1.0, "USD", AdRevenuePrecision.Estimated, "creative");

        adapter.RaisePaid(impression);

        Assert.IsTrue(adapter.IsReady);
        Assert.AreEqual(impression.AdUnitId, received.Value.AdUnitId);
    }

    [Test]
    public void 가짜_전면어댑터는_Load와_Show_호출횟수를_센다()
    {
        var adapter = new FakeFullScreenAdapter();

        adapter.Load();
        adapter.Load();
        adapter.Show();

        Assert.AreEqual(2, adapter.LoadCount);
        Assert.AreEqual(1, adapter.ShowCount);
    }

    [Test]
    public void 가짜_전면어댑터는_Dispose시_IsDisposed를_true로_바꾼다()
    {
        var adapter = new FakeFullScreenAdapter();

        adapter.Dispose();

        Assert.IsTrue(adapter.IsDisposed);
    }

    [Test]
    public void 가짜_배너어댑터는_SetHeight_호출시_Height를_갱신하고_HeightChanged_이벤트를_발화한다()
    {
        var adapter = new FakeBannerAdapter();
        float? received = null;
        adapter.HeightChanged += h => received = h;

        adapter.SetHeight(50f);

        Assert.AreEqual(50f, adapter.Height);
        Assert.AreEqual(50f, received);
    }

    [Test]
    public void 가짜_배너어댑터는_Show와_Hide_호출횟수를_센다()
    {
        var adapter = new FakeBannerAdapter();

        adapter.Show();
        adapter.Show();
        adapter.Hide();

        Assert.AreEqual(2, adapter.ShowCount);
        Assert.AreEqual(1, adapter.HideCount);
    }

    [Test]
    public void 가짜_배너어댑터는_RaisePaid_호출시_Paid_이벤트를_발화한다()
    {
        var adapter = new FakeBannerAdapter();
        AdImpression? received = null;
        adapter.Paid += p => received = p;
        var impression = new AdImpression(AdFormat.Banner, "AdMob", "AdMob", "unit", "placement", "placement",
            0.5, "USD", AdRevenuePrecision.Estimated, null);

        adapter.RaisePaid(impression);

        Assert.AreEqual(impression.AdUnitId, received.Value.AdUnitId);
    }

    [Test]
    public void 가짜_배너어댑터는_Dispose시_IsDisposed를_true로_바꾼다()
    {
        var adapter = new FakeBannerAdapter();

        adapter.Dispose();

        Assert.IsTrue(adapter.IsDisposed);
    }
}
