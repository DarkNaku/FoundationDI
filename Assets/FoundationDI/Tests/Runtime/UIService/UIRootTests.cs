using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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
    public void UIRoot는_카메라가_있으면_ScreenSpaceCamera와_지정정렬거리로_구성한다()
    {
        var camGo = new GameObject("cam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();

        var root = new UIRoot(default, "Default", 7, 33f, () => cam);
        var canvas = root.GO.GetComponent<Canvas>();

        Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
        Assert.AreSame(cam, canvas.worldCamera);
        Assert.AreEqual(7, canvas.sortingOrder);
        Assert.AreEqual(SortingLayer.NameToID("Default"), canvas.sortingLayerID);
        Assert.AreEqual(33f, canvas.planeDistance);

        Object.DestroyImmediate(root.GO);
        Object.DestroyImmediate(camGo);
    }

    [Test]
    public void UIRoot는_카메라가_없으면_ScreenSpaceOverlay로_폴백한다()
    {
        var root = new UIRoot(default, "Default", 0, 100f, () => null);
        var canvas = root.GO.GetComponent<Canvas>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void UIRoot의_CanvasGO는_생성시점_active씬에_소속된다()
    {
        var root = new UIRoot(default, "Default", 0, 100f, () => null);

        Assert.AreEqual(SceneManager.GetActiveScene(), root.GO.scene,
            "DontDestroyOnLoad가 아니라 active 씬에 소속되어야 한다");

        Object.DestroyImmediate(root.GO);
    }
}
