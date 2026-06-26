using System.Collections.Generic;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class PresenterLifecycleTests
{
    private class V : UIView { }
    [UIPrefab("UI/Sample")]
    private class P : UIPagePresenter<V> { }

    [Test]
    public void OnShown_구독자는_AfterShow_발화시_호출된다()
    {
        var p = new P();
        var called = false;
        p.OnShown(_ => called = true);

        p.FireForTest(UIPresenterBase.LifecycleEvent.AfterShow);

        Assert.IsTrue(called);
    }
}
