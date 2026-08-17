using NUnit.Framework;
using DarkNaku.FoundationDI;

public class ModeControllerTests
{
    private class V : UIView { }
    private class A : UIPopupPresenter<V> { }
    private class B : UIPopupPresenter<V> { }
    private class OverlayAbove : UIOverlayPresenter<V> { }
    private class OverlayBelow : UIOverlayPresenter<V> { protected internal override bool Above => false; }

    [Test]
    public void Overlay는_Above와_Below_집합으로_분리된다()
    {
        var c = new OverlayController();
        var above = new OverlayAbove();
        var below = new OverlayBelow();

        c.Register(above, true);
        c.Register(below, false);

        Assert.AreEqual(1, c.Above.Count);
        Assert.AreSame(above, c.Above[0]);
        Assert.AreEqual(1, c.Below.Count);
        Assert.AreSame(below, c.Below[0]);
    }

    [Test]
    public void Popup_스택은_LIFO로_Current를_반환한다()
    {
        var c = new PopupController();
        var a = new A(); var b = new B();
        c.Add(a); c.Add(b);
        Assert.AreSame(b, c.Current);
        c.Remove(b);
        Assert.AreSame(a, c.Current);
    }
}
