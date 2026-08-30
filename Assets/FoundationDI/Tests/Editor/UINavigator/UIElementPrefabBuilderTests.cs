using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;

public class UIElementPrefabBuilderTests
{
    public class DummyView : UIView { }

    private GameObject _built;

    [TearDown]
    public void TearDown()
    {
        if (_built != null) Object.DestroyImmediate(_built);
    }

    [Test]
    public void 모든_모드의_루트는_스트레치_RectTransform과_CanvasGroup과_View를_갖는다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Page);

        var rt = (RectTransform)_built.transform;

        Assert.IsNotNull(_built.GetComponent<CanvasGroup>());
        Assert.IsNotNull(_built.GetComponent<DummyView>());
        Assert.AreEqual(Vector2.zero, rt.anchorMin);
        Assert.AreEqual(Vector2.one, rt.anchorMax);
        Assert.AreEqual(Vector2.zero, rt.offsetMin);
        Assert.AreEqual(Vector2.zero, rt.offsetMax);
    }

    [Test]
    public void Page는_자식_없이_루트만_만든다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Page);

        Assert.AreEqual(0, _built.transform.childCount);
    }

    [Test]
    public void Overlay도_자식_없이_루트만_만들고_blocksRaycasts를_끄지_않는다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Overlay);

        Assert.AreEqual(0, _built.transform.childCount);
        Assert.IsTrue(_built.GetComponent<CanvasGroup>().blocksRaycasts,
            "blocksRaycasts를 끄면 오버레이 안의 버튼까지 죽는다. 전면 배경이 없으므로 입력은 자연히 통과한다.");
    }

    [Test]
    public void Popup은_Background와_Content_자식을_만든다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Popup);

        var background = _built.transform.Find("Background");
        var content = _built.transform.Find("Content");

        Assert.IsNotNull(background, "모달 배경");
        Assert.IsNotNull(content, "실제 팝업 내용이 들어갈 자리");
        Assert.AreEqual(0, background.GetSiblingIndex(), "배경은 내용보다 아래에 그려져야 한다");

        var image = background.GetComponent<Image>();

        Assert.IsNotNull(image);
        Assert.IsTrue(image.raycastTarget, "모달이므로 뒤쪽 입력을 막아야 한다");
    }
}
