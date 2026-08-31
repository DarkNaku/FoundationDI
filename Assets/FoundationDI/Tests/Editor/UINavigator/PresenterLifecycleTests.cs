using System.Collections.Generic;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class PresenterLifecycleTests
{
    private class V : UIView { }
    [UIPrefab("UI/Sample")]
    private class P : UIPagePresenter<V> { }

    [UIPrefab("UI/Configurable")]
    private class ConfigurableP : UIPagePresenter<V>, IConfigurable<int>
    {
        public int Received = -1;
        public void Configure(int parameters) => Received = parameters;
    }

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

    // 아래 셋은 단언이 아니라 "컴파일된다는 사실"이 본체다.
    // 콜백 파라미터나 체인 반환이 카테고리 기반 타입(UIPagePresenter<V>)으로 떨어지면
    // 구체 타입 지역변수로의 대입이 컴파일되지 않는다.

    [Test]
    public void 라이프사이클_콜백은_구체_Presenter_타입을_받는다()
    {
        var p = new P();
        P received = null;
        p.OnAfterShow(x => received = x);

        p.Fire(UIPresenter.LifecycleEvent.AfterShow);

        Assert.AreSame(p, received);
    }

    [Test]
    public void 빌더_체인은_구체_Presenter_타입을_유지한다()
    {
        var p = new P();
        var called = false;

        P chained = p.WithTransition(new NoopTransition()).OnAfterShow(_ => called = true);

        chained.Fire(UIPresenter.LifecycleEvent.AfterShow);

        Assert.AreSame(p, chained);
        Assert.IsTrue(called);
    }

    [Test]
    public void WithParams는_구체_Presenter_타입을_반환한다()
    {
        var p = new ConfigurableP();

        ConfigurableP chained = p.WithParams(42);

        Assert.AreSame(p, chained);
        Assert.AreEqual(42, p.Received);
    }
}
