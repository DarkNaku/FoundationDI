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

    [Test]
    public void CreateDefault의_레이어는_sibling_순서가_렌더_순서와_같다()
    {
        var root = UIRoot.CreateDefault();

        Assert.AreEqual(0, root.PageLayer.GetSiblingIndex());
        Assert.AreEqual(1, root.BelowOverlayLayer.GetSiblingIndex());
        Assert.AreEqual(2, root.PopupLayer.GetSiblingIndex());
        Assert.AreEqual(3, root.AboveOverlayLayer.GetSiblingIndex());

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void CreateDefault는_ScreenSpaceOverlay와_ScaleWithScreenSize_Expand로_구성한다()
    {
        var root = UIRoot.CreateDefault();
        var canvas = root.GO.GetComponent<Canvas>();
        var scaler = root.GO.GetComponent<CanvasScaler>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution, scaler.referenceResolution);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void CreateDefault는_DontDestroyOnLoad를_적용하지_않는다()
    {
        var root = UIRoot.CreateDefault();

        Assert.AreNotEqual("DontDestroyOnLoad", root.GO.scene.name,
            "상주화는 UIService의 책임이다. 에디터 프리팹 조립에도 쓰이므로 여기서 하면 안 된다.");

        Object.DestroyImmediate(root.GO);
    }
}
