using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class FullScreenAdUnitTest
{
    private static readonly AdRetryPolicy Policy = new(maxAttempts: 3, baseSeconds: 2f, maxDelaySeconds: 64f);

    // 테스트마다 반복되는 조립을 한 곳으로 모은다. adsRemoved 기본은 false.
    private static FullScreenAdUnit NewUnit(FakeFullScreenAdapter adapter, FakeAdDispatcher dispatcher,
                                           AdFormat format = AdFormat.Interstitial,
                                           int rewardGraceFrames = 1,
                                           Func<bool> adsRemoved = null)
    {
        return new FullScreenAdUnit(adapter, dispatcher, format, Policy, rewardGraceFrames, adsRemoved);
    }

    [Test]
    public void 로드에_실패하면_지수_백오프_지연으로_재시도한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        sut.Load();
        Assert.AreEqual(1, adapter.LoadCount, "최초 로드가 호출되지 않았다");

        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        dispatcher.Advance(1.9f);
        Assert.AreEqual(1, adapter.LoadCount, "2초 전에 재시도했다");

        dispatcher.Advance(0.2f);   // 누적 2.1초 — 첫 재시도는 2^1 = 2초
        Assert.AreEqual(2, adapter.LoadCount, "2초 후 재시도하지 않았다");

        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        dispatcher.Advance(3.9f);
        Assert.AreEqual(2, adapter.LoadCount, "4초 전에 재시도했다");

        dispatcher.Advance(0.2f);   // 두 번째 재시도는 2^2 = 4초
        Assert.AreEqual(3, adapter.LoadCount);
    }

    [Test]
    public void 최대_재시도_횟수를_초과하면_더_이상_재시도하지_않는다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);   // maxAttempts = 3

        sut.Load();

        // 3번의 재시도를 모두 소진시킨다.
        for (var i = 0; i < 3; i++)
        {
            adapter.RaiseLoadFailed(new AdError(3, "no fill"));
            dispatcher.Advance(200f);
        }

        Assert.AreEqual(4, adapter.LoadCount, "최초 1회 + 재시도 3회여야 한다");

        // 4번째 실패 — 한도를 넘었으므로 재시도가 예약되면 안 된다.
        LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("재시도 후에도 실패"));
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        Assert.AreEqual(0, dispatcher.PendingCount, "한도 초과 후에도 재시도가 예약됐다");

        dispatcher.Advance(200f);
        Assert.AreEqual(4, adapter.LoadCount, "한도 초과 후에도 재시도했다");
    }

    [Test]
    public void 로드에_성공하면_재시도_카운터가_초기화된다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        sut.Load();
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));
        dispatcher.Advance(2.1f);          // 재시도 1회 소진 (2^1)
        adapter.RaiseLoaded();             // 성공 → 카운터 리셋

        var loadCountBefore = adapter.LoadCount;
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        // 리셋됐다면 다음 지연은 다시 2초여야 한다. 리셋 안 됐다면 4초다.
        dispatcher.Advance(2.1f);
        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount,
                        "카운터가 리셋되지 않아 지연이 2초가 아니었다");
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 준비되지_않은_상태의_ShowAsync는_NotReady를_반환하고_로드를_시작한다() =>
        UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter { IsReady = false };
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.NotReady, result.Outcome);
        Assert.AreEqual(0, adapter.ShowCount, "준비도 안 됐는데 Show를 호출했다");
        Assert.AreEqual(1, adapter.LoadCount, "NotReady일 때 로드를 트리거하지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 표시에_실패하면_Failed와_에러를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();

        // Awaitable을 먼저 잡아두고 이벤트를 발화시킨 뒤 await 한다.
        var pending = sut.ShowAsync();
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "no ad to show"));

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Failed, result.Outcome);
        Assert.AreEqual(7, result.Error.Code);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 표시_중에_ShowAsync를_다시_호출하면_Failed를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();

        var first = sut.ShowAsync();
        var second = await sut.ShowAsync();   // 아직 첫 번째가 안 끝났다

        Assert.AreEqual(AdShowOutcome.Failed, second.Outcome);
        Assert.AreEqual(1, adapter.ShowCount, "중복 호출이 Show를 두 번 불렀다");

        // 첫 번째를 정리해서 테스트가 미완료 Awaitable을 남기지 않게 한다.
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(0, "cleanup"));
        await first;
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 표시_중인_배치명이_Paid_임프레션에_찍힌다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();

        AdImpression? received = null;
        sut.Paid += impression => received = impression;

        // Awaitable을 먼저 잡아두고 이벤트를 발화시킨 뒤 await 한다.
        var pending = sut.ShowAsync("some_placement");

        var raised = new AdImpression(AdFormat.Interstitial, "AdMob", "Meta", "unit-1", "network-placement",
                                      null, 1.23, "USD", AdRevenuePrecision.Exact, "creative-9");
        adapter.RaisePaid(raised);

        Assert.IsTrue(received.HasValue, "Paid 이벤트가 발화되지 않았다");
        Assert.AreEqual("some_placement", received.Value.Placement);
        Assert.AreEqual(raised.Format, received.Value.Format);
        Assert.AreEqual(raised.AdPlatform, received.Value.AdPlatform);
        Assert.AreEqual(raised.NetworkName, received.Value.NetworkName);
        Assert.AreEqual(raised.AdUnitId, received.Value.AdUnitId);
        Assert.AreEqual(raised.NetworkPlacement, received.Value.NetworkPlacement);
        Assert.AreEqual(raised.Revenue, received.Value.Revenue, 0.0001);
        Assert.AreEqual(raised.Currency, received.Value.Currency);
        Assert.AreEqual(raised.Precision, received.Value.Precision);
        Assert.AreEqual(raised.CreativeId, received.Value.CreativeId);

        // 정리: 표시 중 발화를 남기지 않는다.
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(0, "cleanup"));
        await pending;
    });

    [Test]
    public void 표시_중이_아닐_때는_어댑터가_채운_배치명을_그대로_보존한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        AdImpression? received = null;
        sut.Paid += impression => received = impression;

        var raised = new AdImpression(AdFormat.Interstitial, "AdMob", "Meta", "unit-1", "network-placement",
                                      "adapter-own-placement", 1.23, "USD", AdRevenuePrecision.Exact, "creative-9");
        adapter.RaisePaid(raised);

        Assert.IsTrue(received.HasValue, "Paid 이벤트가 발화되지 않았다");
        Assert.AreEqual("adapter-own-placement", received.Value.Placement);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 보상_이벤트_후_닫히면_Rewarded와_보상정보를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync("double_coins");

        adapter.RaiseDisplayed();
        adapter.RaiseRewarded(new AdReward("coins", 50));
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);           // 유예 프레임 소진

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
        Assert.AreEqual("coins", result.Reward.Label);
        Assert.AreEqual(50, result.Reward.Amount, 0.001);
    });
}
