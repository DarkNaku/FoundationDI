using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

public class UIRootTests
{
    [Test]
    public void CreateDefault는_4개_레이어가_연결된_UIRoot를_반환한다()
    {
        var root = UIRoot.CreateDefault();

        Assert.IsNotNull(root.PageLayer);
        Assert.IsNotNull(root.BelowOverlayLayer);
        Assert.IsNotNull(root.PopupLayer);
        Assert.IsNotNull(root.AboveOverlayLayer);

        Object.DestroyImmediate(root.GO);
    }
}
