using DarkNaku.FoundationDI;
using NUnit.Framework;

public class UmpConsentFactoryTest
{
    [Test]
    public void 에디터에서는_UMP를_쓰지_않는_구현을_돌려준다()
    {
        // 에디터에서 UMP를 켜도 광고·분석 흐름이 막히지 않아야 한다(네이티브 SDK가 없다).
        var ump = UmpConsentFactory.Create(new FakeAdDispatcher());

        Assert.IsInstanceOf<UnsupportedUmpConsent>(ump);
        Assert.IsFalse(ump.IsSupported);
        Assert.IsTrue(ump.CanRequestAds);
    }
}
