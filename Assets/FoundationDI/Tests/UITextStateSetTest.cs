using DarkNaku.FoundationDI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UITextStateSetTest
{
    private GameObject _tmpGo;
    private GameObject _legacyGo;
    private TextMeshProUGUI _tmp;
    private Text _legacy;
    private Material _material;

    [SetUp]
    public void SetUp()
    {
        _tmpGo = new GameObject("tmp");
        _tmp = _tmpGo.AddComponent<TextMeshProUGUI>();

        _legacyGo = new GameObject("legacy");
        _legacy = _legacyGo.AddComponent<Text>();

        _material = new Material(Shader.Find("UI/Default"));
    }

    [TearDown]
    public void TearDown()
    {
        if (_tmpGo != null) Object.DestroyImmediate(_tmpGo);
        if (_legacyGo != null) Object.DestroyImmediate(_legacyGo);
        if (_material != null) Object.DestroyImmediate(_material);
    }

    [Test]
    public void TMP_타깃의_문자열이_바뀐다()
    {
        var set = new UITextStateSet { Target = _tmp };
        set.Normal = new UITextStateValue { Override = UITextSwap.Text, Text = "시작" };
        set.Disabled = new UITextStateValue { Override = UITextSwap.Text, Text = "잠김" };

        set.Apply(UIButtonState.Disabled);

        Assert.AreEqual("잠김", _tmp.text);
    }

    [Test]
    public void 레거시_Text_타깃의_문자열이_바뀐다()
    {
        var set = new UITextStateSet { Target = _legacy };
        set.Normal = new UITextStateValue { Override = UITextSwap.Text, Text = "시작" };
        set.Disabled = new UITextStateValue { Override = UITextSwap.Text, Text = "잠김" };

        set.Apply(UIButtonState.Disabled);

        Assert.AreEqual("잠김", _legacy.text);
    }

    [Test]
    public void 색은_타깃_종류와_무관하게_Graphic_color에_들어간다()
    {
        var tmpSet = new UITextStateSet { Target = _tmp };
        tmpSet.Normal = new UITextStateValue { Override = UITextSwap.Color, Color = Color.red };

        var legacySet = new UITextStateSet { Target = _legacy };
        legacySet.Normal = new UITextStateValue { Override = UITextSwap.Color, Color = Color.red };

        tmpSet.Apply(UIButtonState.Normal);
        legacySet.Apply(UIButtonState.Normal);

        Assert.AreEqual(Color.red, _tmp.color);
        Assert.AreEqual(Color.red, _legacy.color);
    }

    [Test]
    public void TMP_머티리얼은_fontSharedMaterial에_들어간다()
    {
        var set = new UITextStateSet { Target = _tmp };
        set.Normal = new UITextStateValue { Override = UITextSwap.Material, Material = _material };

        set.Apply(UIButtonState.Normal);

        Assert.AreSame(_material, _tmp.fontSharedMaterial);
    }

    [Test]
    public void 레거시_Text_머티리얼은_material에_들어간다()
    {
        var set = new UITextStateSet { Target = _legacy };
        set.Normal = new UITextStateValue { Override = UITextSwap.Material, Material = _material };

        set.Apply(UIButtonState.Normal);

        Assert.AreSame(_material, _legacy.material);
    }

    [Test]
    public void 텍스트_세트도_Selected_미지정이면_Normal로_떨어진다()
    {
        var set = new UITextStateSet { Target = _tmp };
        set.Normal = new UITextStateValue { Override = UITextSwap.Text, Text = "시작" };
        set.Highlighted = new UITextStateValue { Override = UITextSwap.Text, Text = "호버" };
        set.Selected = new UITextStateValue { Override = UITextSwap.None };

        set.Apply(UIButtonState.Selected);

        Assert.AreEqual("시작", _tmp.text);
    }

    [Test]
    public void 텍스트_타깃이_null이면_예외_없이_아무_일도_하지_않는다()
    {
        var set = new UITextStateSet { Target = null };
        set.Normal = new UITextStateValue { Override = UITextSwap.Text, Text = "시작" };

        Assert.DoesNotThrow(() => set.Apply(UIButtonState.Normal));
    }
}
