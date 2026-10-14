using DarkNaku.FoundationDI;
using NUnit.Framework;

public class DmaConsentMapperTest
{
    [Test]
    public void GDPR_판정이_없으면_보내지_않는다()
    {
        Assert.IsFalse(DmaConsentMapper.TryMap(TcfSignals.None, out _));
    }

    [Test]
    public void GDPR_지역이_아니면_EEA_아님과_허용을_보낸다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(false, null), out var dma));
        Assert.AreEqual(false, dma.Eea);
        Assert.IsTrue(dma.AdUserData);
        Assert.IsTrue(dma.AdPersonalization);
    }

    [Test]
    public void GDPR_지역에서_모두_동의하면_모두_허용한다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, "1111111111"), out var dma));
        Assert.AreEqual(true, dma.Eea);
        Assert.IsTrue(dma.AdUserData);
        Assert.IsTrue(dma.AdPersonalization);
    }

    [Test]
    public void 목적7을_거부하면_광고_사용자_데이터를_거부한다()
    {
        //                                               P1 P2 P3 P4 P5 P6 P7
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, "1111110111"), out var dma));
        Assert.IsFalse(dma.AdUserData);
        Assert.IsTrue(dma.AdPersonalization);
    }

    [Test]
    public void 목적4를_거부하면_광고_개인화를_거부한다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, "1110111111"), out var dma));
        Assert.IsTrue(dma.AdUserData);
        Assert.IsFalse(dma.AdPersonalization);
    }

    [Test]
    public void GDPR_지역인데_목적_문자열이_없으면_모두_거부한다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, null), out var dma));
        Assert.AreEqual(true, dma.Eea);
        Assert.IsFalse(dma.AdUserData);
        Assert.IsFalse(dma.AdPersonalization);
    }
}
