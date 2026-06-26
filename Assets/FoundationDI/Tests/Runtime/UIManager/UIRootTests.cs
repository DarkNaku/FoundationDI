using NUnit.Framework;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIRootTests
{
    [Test]
    public void UIRoot는_4개_레이어를_생성한다()
    {
        var root = new UIRoot();
        Assert.IsNotNull(root.PageLayer);
        Assert.IsNotNull(root.PopupLayer);
        Assert.IsNotNull(root.AboveOverlayLayer);
        Assert.IsNotNull(root.BelowOverlayLayer);
        Object.DestroyImmediate(root.GO);
    }
}
