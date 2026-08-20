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
}
