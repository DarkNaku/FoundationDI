using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
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

    [Test]
    public void UIRoot는_CanvasScaler를_ScaleWithScreenSize_Expand_기준해상도로_구성한다()
    {
        var root = new UIRoot(new Vector2(1080, 1920));
        var scaler = root.GO.GetComponent<CanvasScaler>();

        Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode);
        Assert.AreEqual(new Vector2(1080, 1920), scaler.referenceResolution);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void UIRoot는_항상_ScreenSpaceOverlay로_구성한다()
    {
        var root = new UIRoot();
        var canvas = root.GO.GetComponent<Canvas>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void UIRoot의_CanvasGO는_DontDestroyOnLoad에_소속된다()
    {
        var root = new UIRoot();

        Assert.AreEqual("DontDestroyOnLoad", root.GO.scene.name,
            "캔버스는 씬을 넘어 상주해야 한다(DontDestroyOnLoad)");

        Object.DestroyImmediate(root.GO);
    }
}
