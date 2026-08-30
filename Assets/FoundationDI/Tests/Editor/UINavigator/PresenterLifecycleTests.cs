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

        p.Fire(UIPresenter.LifecycleEvent.AfterShow);

        Assert.IsTrue(called);
    }

    [Test]
    public void OnBeforeShow_구독자는_BeforeShow_발화시_호출된다()
    {
        var p = new P();
        var called = false;
        p.OnBeforeShow(_ => called = true);

        p.Fire(UIPresenter.LifecycleEvent.BeforeShow);

        Assert.IsTrue(called);
    }

    [Test]
    public void OnBeforeHide_구독자는_BeforeHide_발화시_호출된다()
    {
        var p = new P();
        var called = false;
        p.OnBeforeHide(_ => called = true);

        p.Fire(UIPresenter.LifecycleEvent.BeforeHide);

        Assert.IsTrue(called);
    }
}
