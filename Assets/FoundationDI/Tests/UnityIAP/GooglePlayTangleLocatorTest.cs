using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class GooglePlayTangleLocatorTest
{
    private class NamespacedTangle { }
    private class GlobalTangle { }

    private static Func<string, Type> Resolver(Dictionary<string, Type> types)
    {
        return name => types.TryGetValue(name, out var type) ? type : null;
    }

    [Test]
    public void 네임스페이스_안의_GooglePlayTangle만_있어도_찾는다()
    {
        // Unity IAP 5의 Obfuscator는 UnityEngine.Purchasing.Security 네임스페이스 안에 만든다.
        var resolve = Resolver(new Dictionary<string, Type>
        {
            ["UnityEngine.Purchasing.Security.GooglePlayTangle"] = typeof(NamespacedTangle)
        });

        Assert.AreEqual(typeof(NamespacedTangle), GooglePlayTangleLocator.Find(resolve));
    }

    [Test]
    public void 전역_GooglePlayTangle만_있어도_찾는다()
    {
        var resolve = Resolver(new Dictionary<string, Type> { ["GooglePlayTangle"] = typeof(GlobalTangle) });

        Assert.AreEqual(typeof(GlobalTangle), GooglePlayTangleLocator.Find(resolve));
    }

    [Test]
    public void 둘_다_있으면_네임스페이스_안의_것을_먼저_쓴다()
    {
        var resolve = Resolver(new Dictionary<string, Type>
        {
            ["GooglePlayTangle"] = typeof(GlobalTangle),
            ["UnityEngine.Purchasing.Security.GooglePlayTangle"] = typeof(NamespacedTangle)
        });

        Assert.AreEqual(typeof(NamespacedTangle), GooglePlayTangleLocator.Find(resolve));
    }

    [Test]
    public void 둘_다_없으면_null()
    {
        Assert.IsNull(GooglePlayTangleLocator.Find(Resolver(new Dictionary<string, Type>())));
    }

    [Test]
    public void 링커_보존_XML은_존재하는_Tangle_타입만_그_어셈블리에_넣는다()
    {
        var xml = GooglePlayTangleLinkXmlGenerator.BuildLinkXml(new[]
        {
            ("Assembly-CSharp", "UnityEngine.Purchasing.Security.GooglePlayTangle"),
            ("Assembly-CSharp", "GooglePlayTangle")
        });

        StringAssert.Contains("<assembly fullname=\"Assembly-CSharp\" ignoreIfMissing=\"1\">", xml);
        StringAssert.Contains(
            "<type fullname=\"UnityEngine.Purchasing.Security.GooglePlayTangle\" preserve=\"all\" />", xml);
        StringAssert.Contains("<type fullname=\"GooglePlayTangle\" preserve=\"all\" />", xml);
        Assert.AreEqual(1, CountOf(xml, "<assembly "), "같은 어셈블리는 한 번만 연다");
    }

    [Test]
    public void Tangle_타입이_없으면_빈_linker를_만든다()
    {
        var xml = GooglePlayTangleLinkXmlGenerator.BuildLinkXml(Array.Empty<(string, string)>());

        StringAssert.Contains("<linker>", xml);
        Assert.AreEqual(0, CountOf(xml, "<assembly "));
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
