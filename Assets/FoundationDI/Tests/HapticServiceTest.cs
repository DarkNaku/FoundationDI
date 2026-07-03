using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

public class HapticServiceTest
{
    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("HAPTIC_ENABLED");
    }

    [Test]
    public void 활성화_상태에서_Selection_호출시_provider의_Selection을_호출한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Selection();

        provider.Received(1).Selection();
    }
}
