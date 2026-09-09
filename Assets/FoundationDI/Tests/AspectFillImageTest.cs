using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class AspectFillImageTest
{
    private const float ParentWidth = 800f;
    private const float ParentHeight = 600f;

    private GameObject _parentGo;
    private GameObject _childGo;
    private Image _image;
    private Texture2D _texture;
    private Sprite _sprite;

    [SetUp]
    public void SetUp()
    {
        _parentGo = new GameObject("parent", typeof(RectTransform));
        ((RectTransform)_parentGo.transform).sizeDelta = new Vector2(ParentWidth, ParentHeight);

        _childGo = new GameObject("child", typeof(RectTransform));
        _childGo.transform.SetParent(_parentGo.transform, false);

        _image = _childGo.AddComponent<Image>();
        _texture = new Texture2D(100, 100);
    }

    [TearDown]
    public void TearDown()
    {
        if (_childGo != null) Object.DestroyImmediate(_childGo);
        if (_parentGo != null) Object.DestroyImmediate(_parentGo);
        if (_sprite != null) Object.DestroyImmediate(_sprite);
        if (_texture != null) Object.DestroyImmediate(_texture);
    }

    /// <summary>가로:세로가 width:height인 스프라이트를 Image에 물린다.</summary>
    private void GiveSprite(float width, float height)
    {
        _sprite = Sprite.Create(_texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f));
        _image.sprite = _sprite;
    }

    private AspectFillImage NewFitter(AspectFillImage.FitMode mode, TextAnchor alignment)
    {
        var fitter = _childGo.AddComponent<AspectFillImage>();
        fitter.Mode = mode;
        fitter.FitAlignment = alignment;
        fitter.Refresh();
        return fitter;
    }

    // 부모 800x600(4:3)에 2:1 스프라이트를 Fit하면 가로가 꽉 차고(800) 세로는 400이 되어
    // 위아래로 200의 여백이 남는다. 기준 위치는 그 200을 어느 쪽으로 몰지를 정한다.
    [TestCase(TextAnchor.UpperLeft, 100f)]
    [TestCase(TextAnchor.UpperCenter, 100f)]
    [TestCase(TextAnchor.UpperRight, 100f)]
    [TestCase(TextAnchor.MiddleLeft, 0f)]
    [TestCase(TextAnchor.MiddleCenter, 0f)]
    [TestCase(TextAnchor.MiddleRight, 0f)]
    [TestCase(TextAnchor.LowerLeft, -100f)]
    [TestCase(TextAnchor.LowerCenter, -100f)]
    [TestCase(TextAnchor.LowerRight, -100f)]
    public void Fit에서_세로_여백이_기준_위치의_반대쪽으로_몰린다(TextAnchor alignment, float expectedY)
    {
        GiveSprite(100f, 50f);

        var fitter = NewFitter(AspectFillImage.FitMode.Fit, alignment);
        var rect = (RectTransform)fitter.transform;

        Assert.That(rect.sizeDelta.y, Is.EqualTo(-200f).Within(0.01f), "여백 계산이 전제와 다르다");
        Assert.That(rect.anchoredPosition.y, Is.EqualTo(expectedY).Within(0.01f));
        Assert.That(rect.anchoredPosition.x, Is.EqualTo(0f).Within(0.01f), "꽉 찬 축은 움직이지 않는다");
    }

    // 부모 800x600에 1:2 스프라이트를 Fit하면 세로가 꽉 차고(600) 가로는 300이 되어
    // 좌우로 500의 여백이 남는다.
    [TestCase(TextAnchor.UpperLeft, -250f)]
    [TestCase(TextAnchor.MiddleLeft, -250f)]
    [TestCase(TextAnchor.LowerLeft, -250f)]
    [TestCase(TextAnchor.UpperCenter, 0f)]
    [TestCase(TextAnchor.MiddleCenter, 0f)]
    [TestCase(TextAnchor.LowerCenter, 0f)]
    [TestCase(TextAnchor.UpperRight, 250f)]
    [TestCase(TextAnchor.MiddleRight, 250f)]
    [TestCase(TextAnchor.LowerRight, 250f)]
    public void Fit에서_가로_여백이_기준_위치의_반대쪽으로_몰린다(TextAnchor alignment, float expectedX)
    {
        GiveSprite(50f, 100f);

        var fitter = NewFitter(AspectFillImage.FitMode.Fit, alignment);
        var rect = (RectTransform)fitter.transform;

        Assert.That(rect.sizeDelta.x, Is.EqualTo(-500f).Within(0.01f), "여백 계산이 전제와 다르다");
        Assert.That(rect.anchoredPosition.x, Is.EqualTo(expectedX).Within(0.01f));
        Assert.That(rect.anchoredPosition.y, Is.EqualTo(0f).Within(0.01f), "꽉 찬 축은 움직이지 않는다");
    }

    // Cover는 넘치는 쪽을 잘라내는 모드라 여백이 없다. 기준 위치를 지정해도 중앙을 유지해
    // 잘림이 상하(좌우) 균등하게 일어난다.
    [Test]
    public void Cover에서는_기준_위치를_무시하고_중앙에_둔다()
    {
        GiveSprite(100f, 50f);

        var fitter = NewFitter(AspectFillImage.FitMode.Cover, TextAnchor.UpperLeft);
        var rect = (RectTransform)fitter.transform;

        Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
    }

    // 기본값이 중앙이어야 이 필드가 생기기 전에 배치된 기존 오브젝트의 동작이 그대로 유지된다.
    [Test]
    public void 기준_위치의_기본값은_중앙이다()
    {
        GiveSprite(100f, 50f);

        var fitter = _childGo.AddComponent<AspectFillImage>();
        fitter.Mode = AspectFillImage.FitMode.Fit;
        fitter.Refresh();

        var rect = (RectTransform)fitter.transform;

        Assert.That(fitter.FitAlignment, Is.EqualTo(TextAnchor.MiddleCenter));
        Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
    }
}
