using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIServiceSettingsTests
{
    [Test]
    public void UIServiceSettings는_정렬레이어_정렬순서_평면거리를_설정값으로_반환한다()
    {
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        var so = new SerializedObject(settings);
        so.FindProperty("_sortingLayerName").stringValue = "UI";
        so.FindProperty("_sortingOrder").intValue = 5;
        so.FindProperty("_planeDistance").floatValue = 42f;
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreEqual("UI", settings.SortingLayerName);
        Assert.AreEqual(5, settings.SortingOrder);
        Assert.AreEqual(42f, settings.PlaneDistance);

        Object.DestroyImmediate(settings);
    }
}
