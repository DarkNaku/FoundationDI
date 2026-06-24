using System;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class UIPrefabKeyResolverTests
{
    [UIPrefab("UI/MainMenu")]
    private class Tagged { }
    private class Untagged { }

    [Test]
    public void 속성에_선언된_키를_반환한다()
        => Assert.AreEqual("UI/MainMenu", UIPrefabKeyResolver.Resolve(typeof(Tagged)));

    [Test]
    public void 속성이_없으면_예외를_던진다()
        => Assert.Throws<InvalidOperationException>(() => UIPrefabKeyResolver.Resolve(typeof(Untagged)));
}
