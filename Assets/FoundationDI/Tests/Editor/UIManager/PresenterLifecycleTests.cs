using System.Collections.Generic;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class PresenterLifecycleTests
{
    private class V : UIView { }
    [UIPrefab("UI/Sample")]
    private class P : UIPagePresenter<V> { }

    [Test]
    public void OnAfterShow_구독자는_AfterShow_발화시_호출된다()
    {
        var p = new P();
        var called = false;
        p.OnAfterShow(_ => called = true);

        p.Fire(UIPresenterBase.LifecycleEvent.AfterShow);

        Assert.IsTrue(called);
    }
}
