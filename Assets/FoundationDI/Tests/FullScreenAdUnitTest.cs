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
}
