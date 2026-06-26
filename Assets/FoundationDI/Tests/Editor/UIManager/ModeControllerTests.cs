using NUnit.Framework;
using DarkNaku.FoundationDI;

public class ModeControllerTests
{
    private class V : UIView { }
    private class A : UIPopupPresenter<V> { }
    private class B : UIPopupPresenter<V> { }

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
