using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIServiceSettingsTests
{
    [Test]
    public void UIServiceSettings는_기준해상도를_설정값으로_반환한다()
    {
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        var so = new SerializedObject(settings);
        so.FindProperty("_referenceResolution").vector2Value = new Vector2(1080f, 1920f);
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreEqual(new Vector2(1080f, 1920f), settings.ReferenceResolution);

        Object.DestroyImmediate(settings);
    }
}
