using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;

public class UIRootPrefabCreatorTests
{
    private const string Path = "Assets/__UIRootPrefabCreatorTests__.prefab";

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Path);

    [Test]
    public void 생성된_프리팹은_4개_레이어가_자기_자식으로_연결되어_있다()
    {
        var asset = UIRootPrefabCreator.CreateAt(Path);

        Assert.IsNotNull(asset, "프리팹 에셋의 UIRoot를 반환해야 한다");
        Assert.IsNotNull(asset.PageLayer);
        Assert.IsNotNull(asset.BelowOverlayLayer);
        Assert.IsNotNull(asset.PopupLayer);
        Assert.IsNotNull(asset.AboveOverlayLayer);

        // SaveAsPrefabAsset이 참조를 에셋 내부로 리매핑했는지 — 씬 오브젝트를 가리키면 안 된다.
        Assert.AreSame(asset.transform, asset.PageLayer.parent);
        Assert.AreSame(asset.transform, asset.AboveOverlayLayer.parent);
    }

    [Test]
    public void 생성된_프리팹은_CreateDefault와_같은_캔버스_구성을_갖는다()
    {
        var asset = UIRootPrefabCreator.CreateAt(Path);
        var scaler = asset.GetComponent<CanvasScaler>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, asset.GetComponent<Canvas>().renderMode);
        Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution, scaler.referenceResolution);
    }

    [Test]
    public void 프리팹을_만든_뒤_씬에_임시_오브젝트가_남지_않는다()
    {
        UIRootPrefabCreator.CreateAt(Path);

        var leftovers = Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None);

        Assert.AreEqual(0, leftovers.Length, "조립용 임시 GameObject는 저장 후 파괴되어야 한다");
    }
}
