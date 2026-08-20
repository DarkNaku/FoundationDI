using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class FullScreenAdUnitTest
{
    private static readonly AdRetryPolicy Policy = new(maxAttempts: 3, baseSeconds: 2f, maxDelaySeconds: 64f);

    // 테스트마다 반복되는 조립을 한 곳으로 모은다. adsRemoved 기본은 false, cooldownSeconds 기본은 0(쿨다운 없음).
    private static FullScreenAdUnit NewUnit(FakeFullScreenAdapter adapter, FakeAdDispatcher dispatcher,
                                           AdFormat format = AdFormat.Interstitial,
                                           int rewardGraceFrames = 1,
                                           float cooldownSeconds = 0f,
                                           Func<bool> adsRemoved = null)
    {
        return new FullScreenAdUnit(adapter, dispatcher, format, Policy, rewardGraceFrames, cooldownSeconds, adsRemoved);
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
    public void 재시도_한도_소진_후_명시적_Load_호출은_재시도_카운터를_초기화한다()
    {
        // 초기 자동로드가 네트워크 없이 예산을 전부 소진하면, 리셋 없이는 닫힘 후
        // 자동 재로드나 NotReady의 ShowAsync가 트리거하는 Load()가 매번 딱 1회
        // 시도 후 곧장 에러 로그로 끝나 버린다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);   // maxAttempts = 3

        sut.Load();
        for (var i = 0; i < 3; i++)
        {
            adapter.RaiseLoadFailed(new AdError(3, "no fill"));
            dispatcher.Advance(200f);
        }

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("재시도 후에도 실패"));
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));   // 예산 소진 — 더 이상 재시도 예약 안 됨

        var loadCountBeforeExplicitLoad = adapter.LoadCount;

        sut.Load();   // 명시적 재호출 — 카운터가 리셋돼야 한다
        Assert.AreEqual(loadCountBeforeExplicitLoad + 1, adapter.LoadCount,
                        "명시적 Load가 어댑터를 다시 호출하지 않았다");

        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        dispatcher.Advance(1.9f);
        Assert.AreEqual(loadCountBeforeExplicitLoad + 1, adapter.LoadCount,
                        "리셋 후 첫 재시도가 2초 전에 발생했다");

        dispatcher.Advance(0.2f);   // 누적 2.1초 — 리셋됐다면 2^1 = 2초 지연이어야 한다
        Assert.AreEqual(loadCountBeforeExplicitLoad + 2, adapter.LoadCount,
                        "한도 소진 후 명시적 Load가 재시도 카운터를 초기화하지 않았다");
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

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 보상없이_닫히면_Dismissed를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Dismissed, result.Outcome);
        Assert.IsTrue(result.WasShown, "노출은 됐으므로 WasShown이어야 한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 전면광고는_보상없이_닫히면_Shown을_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Shown, result.Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 닫힘이_보상보다_먼저_와도_유예_프레임_안에서_Rewarded로_확정된다() =>
        UniTask.ToCoroutine(async () =>
    {
        // 일부 미디에이션 네트워크가 실제로 이 순서로 이벤트를 보낸다.
        // 유예 프레임이 없으면 유저가 광고를 다 봤는데도 보상을 잃는다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, rewardGraceFrames: 1);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();                              // 닫힘이 먼저
        adapter.RaiseRewarded(new AdReward("coins", 10));   // 보상이 나중
        dispatcher.TickFrames(1);

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
        Assert.AreEqual(10, result.Reward.Amount, 0.001);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 유예_프레임이_0이면_닫힘_즉시_확정한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, rewardGraceFrames: 0);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseRewarded(new AdReward("coins", 10));
        adapter.RaiseClosed();   // TickFrames 없이 바로 확정돼야 한다

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 늦게_도착한_보상이_다음_쇼로_새지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, rewardGraceFrames: 1);

        // 첫 번째 쇼: 보상 없이 닫힘 → Dismissed로 확정되고 래치가 비워진다.
        adapter.RaiseLoaded();
        var firstPending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var firstResult = await firstPending;
        Assert.AreEqual(AdShowOutcome.Dismissed, firstResult.Outcome);

        // 확정 이후 뒤늦게 도착하는 보상 — 유예 프레임을 노리는 바로 그 SDK 오동작이다.
        adapter.RaiseRewarded(new AdReward("coins", 999));

        // 두 번째 쇼: 보상 없이 닫힌다. ShowAsync가 래치를 리셋하지 않으면 이 쇼가
        // 첫 번째 쇼의 늦은 보상을 가로채 Rewarded로 잘못 확정된다.
        adapter.RaiseLoaded();
        var secondPending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var secondResult = await secondPending;

        Assert.AreEqual(AdShowOutcome.Dismissed, secondResult.Outcome,
                         "이전 쇼의 늦은 보상이 다음 쇼로 샜다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 표시_실패_전에_래치된_보상이_다음_쇼로_새지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, rewardGraceFrames: 1);

        // 첫 번째 쇼: 보상이 래치된 뒤 표시 실패 — Closed가 오지 않으므로 래치를
        // 쓸어낼 기회가 FinalizeClose 경로에는 없다.
        adapter.RaiseLoaded();
        var firstPending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseRewarded(new AdReward("coins", 50));
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "no ad to show"));

        var firstResult = await firstPending;
        Assert.AreEqual(AdShowOutcome.Failed, firstResult.Outcome);

        // 두 번째 쇼: 보상 없이 닫힌다. 래치가 새면 Rewarded가 잘못 나온다.
        adapter.RaiseLoaded();
        var secondPending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var secondResult = await secondPending;

        Assert.AreEqual(AdShowOutcome.Dismissed, secondResult.Outcome,
                         "표시 실패 전에 래치된 보상이 다음 쇼로 샜다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 중복된_Closed는_Closed_이벤트를_한_번만_발화한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, rewardGraceFrames: 1);

        var closedCount = 0;
        sut.Closed += () => closedCount++;

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);           // 첫 확정 — Closed가 1회 발화해야 한다

        var result = await pending;
        Assert.AreEqual(AdShowOutcome.Shown, result.Outcome);

        adapter.RaiseClosed();              // 중복 Closed — 어댑터/네트워크 오동작
        dispatcher.TickFrames(1);           // 두 번째 확정 시도 — Complete가 false를 반환해야 한다

        Assert.AreEqual(1, closedCount, "중복 Closed가 Closed 이벤트를 다시 발화시켰다");
    });

    [Test]
    public void Dispose_중_예약된_닫힘_확정을_취소한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, rewardGraceFrames: 1);

        var closedFired = false;
        sut.Closed += () => closedFired = true;

        adapter.RaiseLoaded();
        _ = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();   // 유예 예약, 아직 틱은 안 했다

        sut.Dispose();

        dispatcher.TickFrames(1);

        Assert.IsFalse(closedFired, "Dispose 후에도 예약된 확정이 발화했다");
        Assert.AreEqual(0, dispatcher.PendingCount, "Dispose가 예약된 확정을 취소하지 않았다");
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 늦은_Closed는_다음_쇼를_확정시키지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, rewardGraceFrames: 1);

        var closedCount = 0;
        sut.Closed += () => closedCount++;

        // 쇼 1: 표시 실패로 끝난다.
        adapter.RaiseLoaded();
        var firstPending = sut.ShowAsync();

        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "no ad to show"));

        var firstResult = await firstPending;
        Assert.AreEqual(AdShowOutcome.Failed, firstResult.Outcome);

        // AdMob은 DisplayFailed 이후에도 Closed를 보낼 수 있다. 틱하지 않은 채로 남겨둔다.
        adapter.RaiseClosed();

        // 쇼 2 시작 — ShowAsync가 쇼 1의 늦은 Closed 예약을 버려야 한다.
        adapter.RaiseLoaded();
        var secondPending = sut.ShowAsync();

        adapter.RaiseDisplayed();

        // 버려지지 않았다면 이 틱에서 쇼 1의 늦은 Closed가 쇼 2를 새치기해 확정시켜 버린다.
        dispatcher.TickFrames(1);

        Assert.AreEqual(0, closedCount, "쇼 1의 늦은 Closed가 확정을 일으켰다(쇼 2가 새치기당했다)");

        // 쇼 2를 정상적으로 마무리한다.
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var secondResult = await secondPending;

        Assert.AreEqual(AdShowOutcome.Shown, secondResult.Outcome);
        Assert.AreEqual(1, closedCount, "쇼 2 확정에서 Closed 이벤트가 정확히 한 번 발화하지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose는_대기_중인_ShowAsync를_Failed로_완료시킨다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        sut.Dispose();

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Failed, result.Outcome);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고가_닫히면_다음_광고를_자동으로_로드한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var loadCountBefore = adapter.LoadCount;

        var pending = sut.ShowAsync();
        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount, "닫힘 후 자동 재로드가 없었다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 표시에_실패하면_다음_광고를_자동으로_로드한다() => UniTask.ToCoroutine(async () =>
    {
        // 표시 실패는 대개 만료·소진된 광고가 원인이라 즉시 새로 받아와야 한다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var loadCountBefore = adapter.LoadCount;

        var pending = sut.ShowAsync();
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "expired"));
        await pending;

        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount, "표시 실패 후 재로드가 없었다");
    });

    [Test]
    public void Dispose는_어댑터를_정리하고_예약된_재시도를_취소한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        sut.Load();
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));
        Assert.AreEqual(1, dispatcher.PendingCount, "재시도가 예약되지 않았다");

        sut.Dispose();

        Assert.IsTrue(adapter.IsDisposed, "어댑터가 해제되지 않았다");
        Assert.AreEqual(0, dispatcher.PendingCount, "예약된 재시도가 취소되지 않았다");

        var loadCountBefore = adapter.LoadCount;
        dispatcher.Advance(200f);
        Assert.AreEqual(loadCountBefore, adapter.LoadCount, "해제 후에도 재시도가 실행됐다");
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 해제된_뒤의_ShowAsync는_Failed를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();
        sut.Dispose();

        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, adapter.ShowCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 중복된_표시_실패는_재로드를_한_번만_한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var loadCountBefore = adapter.LoadCount;

        var pending = sut.ShowAsync();

        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "expired"));

        // 중복/지연 DisplayFailed — 어댑터/네트워크 오동작. 경고 로그는 두 번 다 찍혀야 한다.
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "expired"));

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Failed, result.Outcome);
        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount, "중복된 표시 실패가 재로드를 두 번 트리거했다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거_상태에서_전면광고는_Blocked를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, adsRemoved: () => true);

        adapter.RaiseLoaded();
        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.Blocked, result.Outcome);
        Assert.AreEqual(0, adapter.ShowCount, "차단됐는데 Show가 호출됐다");
        Assert.IsFalse(result.WasShown);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거_상태에서도_보상형_광고는_정상_표시된다() => UniTask.ToCoroutine(async () =>
    {
        // 보상형은 유저가 자발적으로 보는 것이라 광고제거 대상이 아니다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, adsRemoved: () => true);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        Assert.AreEqual(1, adapter.ShowCount, "보상형이 차단됐다");

        adapter.RaiseRewarded(new AdReward("coins", 5));
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var result = await pending;
        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
    });

    // ---- 전면 쿨다운 게이트 ----

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 전면광고는_표시_후_쿨다운_동안_재표시가_Blocked된다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, cooldownSeconds: 120f);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();
        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        adapter.RaiseLoaded();   // 닫힘 후 자동 재로드가 완료됐다고 가정한다
        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.Blocked, result.Outcome);
        Assert.AreEqual(1, adapter.ShowCount, "쿨다운 중인데 Show가 다시 호출됐다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 쿨다운이_지나면_전면광고가_다시_표시된다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, cooldownSeconds: 120f);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();
        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        adapter.RaiseLoaded();
        dispatcher.Advance(120f);

        var showCountBefore = adapter.ShowCount;
        var second = sut.ShowAsync();

        Assert.AreEqual(showCountBefore + 1, adapter.ShowCount, "쿨다운이 지났는데도 Show가 호출되지 않았다");

        // 정리: 표시 중 발화를 남기지 않는다.
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(0, "cleanup"));
        await second;
    });

    // "보상형은 쿨다운 대상이 아니다"는 이 클래스가 보장하지 않는다 — FullScreenAdUnit은
    // cooldownSeconds를 포맷과 무관하게 그대로 적용한다(생성자에 120을 주면 Rewarded든
    // Interstitial이든 똑같이 쿨다운이 걸린다). 포맷별 면제는 AdService.BuildAdUnits가
    // 보상형에 0을 조립해 넣어서 만든다 — 그 보장은 AdServiceTest의
    // "설정된_전면_쿨다운은_보상형에는_적용되지_않는다"가 실제 조립 경로로 검증한다.

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator CanShow은_쿨다운_동안_거짓이고_지나면_참이다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, cooldownSeconds: 120f);

        adapter.RaiseLoaded();
        Assert.IsTrue(sut.CanShow, "로드 직후에는 표시 가능해야 한다");

        var pending = sut.ShowAsync();
        adapter.RaiseDisplayed();

        Assert.IsFalse(sut.CanShow, "표시 직후 쿨다운 중인데 CanShow가 참이다");

        adapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        adapter.RaiseLoaded();
        Assert.IsFalse(sut.CanShow, "쿨다운이 남아있는데 CanShow가 참이다");

        dispatcher.Advance(120f);
        Assert.IsTrue(sut.CanShow, "쿨다운이 끝났는데도 CanShow가 거짓이다");
    });

    [Test]
    public void CanShow은_광고제거_상태의_전면광고에서_거짓이다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, adsRemoved: () => true);

        adapter.RaiseLoaded();

        Assert.IsFalse(sut.CanShow, "광고제거 상태인데 CanShow가 참이다");
    }

    [Test]
    public void CanShow은_준비되지_않았으면_거짓이다()
    {
        var adapter = new FakeFullScreenAdapter { IsReady = false };
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        Assert.IsFalse(sut.CanShow, "준비 안 됐는데 CanShow가 참이다");
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 쿨다운이_0이면_표시_직후에도_다시_표시할_수_있다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, cooldownSeconds: 0f);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();
        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        adapter.RaiseLoaded();

        Assert.IsTrue(sut.CanShow, "쿨다운이 0인데도 CanShow가 거짓이다");
        Assert.AreEqual(0, dispatcher.PendingCount, "쿨다운이 0인데 타이머가 예약됐다");
    });

    [Test]
    public void Dispose는_쿨다운_타이머를_취소한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, cooldownSeconds: 120f);

        adapter.RaiseLoaded();
        _ = sut.ShowAsync();
        adapter.RaiseDisplayed();   // 쿨다운 타이머가 예약된다

        sut.Dispose();

        Assert.AreEqual(0, dispatcher.PendingCount, "Dispose가 쿨다운 타이머를 취소하지 않았다");

        Assert.DoesNotThrow(() => dispatcher.Advance(200f),
                            "취소되지 않은 쿨다운 콜백이 해제된 유닛을 건드려 예외를 던졌다");
    }
}
